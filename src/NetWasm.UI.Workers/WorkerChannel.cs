using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace NetWasm.UI.Workers;

/// <summary>The workers behind one registered contract. Used by generated proxies.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class WorkerChannel : IDisposable
{
    private readonly string worker;
    private readonly bool pool;
    private readonly int[] handles;
    private readonly bool[] busy;
    private readonly Queue<Waiter> waiters = new();
    private bool disposed;

    private WorkerChannel(string worker, int size, bool pool, bool terminateOnCancel)
    {
        if (string.IsNullOrEmpty(worker))
        {
            throw new ArgumentException("The worker name is missing: set [WorkerContract(Worker = \"...\")] or pass it to AddWorker.", nameof(worker));
        }

        this.worker = worker;
        this.pool = pool;
        TerminateOnCancel = terminateOnCancel;
        handles = new int[size];
        busy = new bool[size];
    }

    internal bool TerminateOnCancel { get; }

    /// <summary>A channel to one dedicated worker that runs all calls.</summary>
    public static WorkerChannel Dedicated(string? worker, bool terminateOnCancel) =>
        new(worker ?? string.Empty, 1, pool: false, terminateOnCancel);

    /// <summary>A channel to a pool of workers that run one call each.</summary>
    public static WorkerChannel Pool(string? worker, int size, bool terminateOnCancel) =>
        new(worker ?? string.Empty, size > 0 ? size : Worker.DefaultPoolSize, pool: true, terminateOnCancel);

    /// <summary>Starts a call: waits for a worker (pools) and allocates the call.</summary>
    public ValueTask<WorkerCall> BeginAsync(CancellationToken cancellationToken)
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(WorkerChannel));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!pool)
        {
            return new(new WorkerCall(this, 0, Handle(0)));
        }

        for (var i = 0; i < busy.Length; i++)
        {
            if (!busy[i])
            {
                busy[i] = true;
                return new(new WorkerCall(this, i, Handle(i)));
            }
        }

        return Wait(cancellationToken);
    }

    private async ValueTask<WorkerCall> Wait(CancellationToken cancellationToken)
    {
        var waiter = new Waiter();
        waiters.Enqueue(waiter);
        using (cancellationToken.Register(static w => ((Waiter)w!).TrySetCanceled(), waiter))
        {
            var slot = await waiter.Task.ConfigureAwait(false);
            return new WorkerCall(this, slot, Handle(slot));
        }
    }

    private int Handle(int slot)
    {
        if (handles[slot] == 0)
        {
            handles[slot] = WorkerCall.Js.Create(worker);
        }

        return handles[slot];
    }

    internal void Release(int slot)
    {
        if (!pool)
        {
            return;
        }

        while (waiters.Count > 0)
        {
            if (waiters.Dequeue().TrySetResult(slot))
            {
                return;
            }
        }

        busy[slot] = false;
    }

    /// <summary>Stops the workers (calls in progress finish first).</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        foreach (var handle in handles)
        {
            if (handle != 0)
            {
                WorkerCall.Js.Dispose(handle);
            }
        }

        while (waiters.Count > 0)
        {
            waiters.Dequeue().TrySetException(new ObjectDisposedException(nameof(WorkerChannel)));
        }
    }

    /// <summary>
    /// Streams the items of a worker method that returns <see cref="IAsyncEnumerable{T}"/>. <paramref name="start"/> stages
    /// the arguments and invokes the worker; <paramref name="take"/> reads one item. Leaving the loop early cancels the
    /// worker's enumeration.
    /// </summary>
    public async IAsyncEnumerable<T> Stream<T>(Func<WorkerCall, Task> start, Func<T> take, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var call = await BeginAsync(cancellationToken).ConfigureAwait(false);
        var items = new Queue<T>();
        TaskCompletionSource<bool>? signal = null;
        call.Item = () =>
        {
            items.Enqueue(take());
            signal?.TrySetResult(true);
        };
        call.Track();

        Task done;
        try
        {
            done = start(call);
        }
        catch
        {
            call.End();
            throw;
        }

        var consumed = 0;
        try
        {
            while (true)
            {
                if (items.Count > 0)
                {
                    var item = items.Dequeue();
                    if (++consumed == WorkerCall.CreditBatch)
                    {
                        consumed = 0;
                        WorkerCall.Js.Credit(call.Handle, call.Id, WorkerCall.CreditBatch);
                    }

                    yield return item;
                    continue;
                }

                if (done.IsCompleted)
                {
                    await WorkerCall.Map(done).ConfigureAwait(false);
                    yield break;
                }

                signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                await Task.WhenAny(signal.Task, done).WaitAsync(cancellationToken).ConfigureAwait(false);
                signal = null;
            }
        }
        finally
        {
            if (done.IsCompleted)
            {
                call.End();
            }
            else
            {
                // Cancelled, or the consumer stopped early: stop the worker's loop too.
                call.Cancel(done);
            }
        }
    }

    private sealed class Waiter : TaskCompletionSource<int>
    {
        public Waiter()
            : base(TaskCreationOptions.RunContinuationsAsynchronously)
        {
        }
    }
}
