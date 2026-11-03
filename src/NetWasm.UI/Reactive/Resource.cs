using System;
using System.Threading;
using System.Threading.Tasks;

namespace NetWasm.UI.Reactive;

/// <summary>Where a <see cref="Resource{T}"/> is in its load cycle.</summary>
public enum ResourceState
{
    /// <summary>Nothing requested yet (a keyed resource whose source is null).</summary>
    Unresolved,

    /// <summary>The first load is running; there is no value yet.</summary>
    Pending,

    /// <summary>The last load succeeded (or <see cref="Resource{T}.Mutate"/> set a value).</summary>
    Ready,

    /// <summary>A reload is running; <see cref="Resource{T}.Latest"/> still holds the previous value.</summary>
    Refreshing,

    /// <summary>The last load failed; see <see cref="Resource{T}.Error"/>.</summary>
    Errored,
}

/// <summary>Factory methods for <see cref="Resource{T}"/>.</summary>
public static class Resource
{
    /// <summary>A resource that loads once (and again on <see cref="Resource{T}.Refetch"/>).</summary>
    public static Resource<T> Create<T>(Func<CancellationToken, Task<T>> fetcher) => new(() => fetcher);

    /// <summary>
    /// A resource that reloads whenever <paramref name="source"/> changes (it is read reactively).
    /// While the source is <c>null</c> nothing is loaded and the state is <see cref="ResourceState.Unresolved"/>.
    /// A load still running when the source changes is cancelled and its result ignored.
    /// </summary>
    public static Resource<T> Create<TSource, T>(Func<TSource?> source, Func<TSource, CancellationToken, Task<T>> fetcher) =>
        new(() =>
        {
            var key = source();
            if (key is null)
            {
                return null;
            }

            return ct => fetcher(key, ct);
        });

    /// <summary>
    /// <see cref="Create{TSource, T}(Func{TSource}, Func{TSource, CancellationToken, Task{T}})"/> for a
    /// value-type key: return <c>null</c> from <paramref name="source"/> to load nothing.
    /// </summary>
    public static Resource<T> Create<TSource, T>(Func<TSource?> source, Func<TSource, CancellationToken, Task<T>> fetcher)
        where TSource : struct =>
        new(() =>
        {
            var key = source();
            if (key is not { } value)
            {
                return null;
            }

            return ct => fetcher(value, ct);
        });
}

/// <summary>
/// Async data as reactive state: loads with a <see cref="Task{T}"/> and exposes <see cref="State"/>,
/// <see cref="Value"/>, <see cref="Loading"/> and <see cref="Error"/> as tracked reads, so bindings
/// update when the load finishes. Created while building, it is disposed (and any running load is
/// cancelled) with its owner scope, and it registers its loads with the nearest <see cref="Suspense"/>.
/// <code>
/// var user = Resource.Create(() => userId.Value, (id, ct) => api.GetUserAsync(id, ct));
/// // Text(() => user.Loading ? "Loading…" : user.Value!.Name)
/// </code>
/// </summary>
public sealed class Resource<T> : IReadOnlySignal<T?>, IDisposable
{
    private readonly Signal<int> version = new(0);
    private readonly Effect tracker;
    private readonly SuspenseRegistry? suspense;
    private Func<CancellationToken, Task<T>>? fetcher;
    private CancellationTokenSource? cts;
    private int loadId;
    private bool counted;
    private bool disposed;
    private T? latest;
    private Exception? error;
    private ResourceState state;

    internal Resource(Func<Func<CancellationToken, Task<T>>?> request)
    {
        if (Scope.Current is { } scope)
        {
            if (scope.TryGetContext(SuspenseRegistry.Key, out var registry))
            {
                suspense = (SuspenseRegistry?)registry;
            }

            scope.Register(this);
        }

        tracker = new Effect(() =>
        {
            var next = request();
            Tracker.Untracked(() => Start(next));
        });
    }

    /// <summary>The loaded value (tracked). Throws the load error when <see cref="State"/> is <see cref="ResourceState.Errored"/>, so a failed load reaches the nearest <see cref="ErrorBoundary"/>.</summary>
    public T? Value
    {
        get
        {
            _ = version.Value;
            if (state == ResourceState.Errored)
            {
                throw new ResourceException(error!);
            }

            return latest;
        }
    }

    /// <summary>The most recent successful value (tracked). Never throws.</summary>
    public T? Latest
    {
        get
        {
            _ = version.Value;
            return latest;
        }
    }

    public ResourceState State
    {
        get
        {
            _ = version.Value;
            return state;
        }
    }

    /// <summary>True while a load is running (first load or reload).</summary>
    public bool Loading
    {
        get
        {
            _ = version.Value;
            return state == ResourceState.Pending || state == ResourceState.Refreshing;
        }
    }

    /// <summary>The error from the last load, or null.</summary>
    public Exception? Error
    {
        get
        {
            _ = version.Value;
            return error;
        }
    }

    /// <summary>Runs the fetcher again with the current source.</summary>
    public void Refetch()
    {
        if (!disposed)
        {
            Start(fetcher);
        }
    }

    /// <summary>Sets the value locally (optimistic update) and cancels any running load.</summary>
    public void Mutate(T value)
    {
        if (disposed)
        {
            return;
        }

        Cancel();
        latest = value;
        error = null;
        Settle(ResourceState.Ready);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        tracker.Dispose();
        Cancel();
        Uncount();
    }

    private void Start(Func<CancellationToken, Task<T>>? next)
    {
        fetcher = next;
        Cancel();
        if (next is null)
        {
            latest = default;
            error = null;
            Settle(ResourceState.Unresolved);
            return;
        }

        var id = ++loadId;
        cts = new CancellationTokenSource();
        Task<T> task;
        try
        {
            task = next(cts.Token);
        }
        catch (Exception ex)
        {
            task = Task.FromException<T>(ex);
        }

        if (task.IsCompleted)
        {
            Complete(id, task);
            return;
        }

        if (!counted)
        {
            counted = true;
            suspense?.Add();
        }

        state = state == ResourceState.Ready || state == ResourceState.Refreshing ? ResourceState.Refreshing : ResourceState.Pending;
        version.Touch();
        _ = Await(id, task);
    }

    private async Task Await(int id, Task<T> task)
    {
        try
        {
            await task;
        }
        catch (Exception)
        {
            // Observed below through the task itself.
        }

        Complete(id, task);
    }

    private void Complete(int id, Task<T> task)
    {
        if (disposed || id != loadId)
        {
            return;
        }

        cts = null;
        if (task.Status == TaskStatus.RanToCompletion)
        {
            latest = task.Result;
            error = null;
            Settle(ResourceState.Ready);
            return;
        }

        error = task.Exception?.InnerException ?? new OperationCanceledException();
        Settle(ResourceState.Errored);
    }

    private void Settle(ResourceState next)
    {
        state = next;
        Scheduler.Batch(() =>
        {
            version.Touch();
            Uncount();
        });
    }

    private void Cancel()
    {
        loadId++;
        var source = cts;
        cts = null;
        source?.Cancel();
    }

    private void Uncount()
    {
        if (counted)
        {
            counted = false;
            suspense?.Done();
        }
    }

    /// <summary>The current value, formatted with the invariant culture (tracked); empty while not loaded.</summary>
    public override string ToString() => TemplateRuntime.Str(Value) ?? string.Empty;
}

/// <summary>Thrown when reading <see cref="Resource{T}.Value"/> after the load failed.</summary>
public sealed class ResourceException : Exception
{
    public ResourceException(Exception inner)
        : base(inner.Message, inner)
    {
    }
}

/// <summary>Counts the loads pending under a <see cref="Suspense"/>.</summary>
internal sealed class SuspenseRegistry
{
    internal static readonly object Key = new();

    private readonly Signal<int> pending = new(0);

    public int Pending => pending.Value;

    public void Add() => pending.Value++;

    public void Done() => pending.Value--;
}
