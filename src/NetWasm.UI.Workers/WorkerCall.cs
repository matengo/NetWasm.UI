using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices.JavaScript;
using System.Threading;
using System.Threading.Tasks;

namespace NetWasm.UI.Workers;

/// <summary>
/// One call from the app to a worker. Used by generated proxies: they stage the arguments (<c>Arg*</c>) and invoke
/// (<c>Invoke*</c>) without an await in between, then await <see cref="Complete{T}(Task{T}, CancellationToken)"/>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class WorkerCall
{
    internal const int CreditBatch = 8;

    private static readonly Dictionary<int, WorkerCall> Active = new();
    private static int nextId;
    private static JSSubscription? subscription;

    private readonly WorkerChannel channel;
    private readonly int slot;
    private bool ended;

    internal WorkerCall(WorkerChannel channel, int slot, int handle)
    {
        this.channel = channel;
        this.slot = slot;
        Handle = handle;
        Id = ++nextId;
        subscription ??= Js.Subscribe(Dispatch);
    }

    /// <summary>The call's id, unique in the app.</summary>
    public int Id { get; }

    internal int Handle { get; }

    internal Action? Progress { get; set; }

    internal Action? Item { get; set; }

    /// <summary>Reports the worker's progress notifications to <paramref name="progress"/>, read with <paramref name="take"/>.</summary>
    public void OnProgress<T>(IProgress<T>? progress, Func<T> take)
    {
        if (progress is not null)
        {
            Progress = () => progress.Report(take());
            Active[Id] = this;
        }
    }

    private static void Dispatch(int callId, int channel)
    {
        if (Active.TryGetValue(callId, out var call))
        {
            (channel == 0 ? call.Progress : call.Item)?.Invoke();
        }
    }

    /// <summary>Awaits the worker's result; maps cancellation to the worker and failures to <see cref="WorkerException"/>.</summary>
    public async Task<T> Complete<T>(Task<T> task, CancellationToken cancellationToken)
    {
        var cancelled = false;
        try
        {
            return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && !task.IsCompleted)
        {
            cancelled = true;
            Cancel(task);
            throw;
        }
        catch (Exception e) when (e is not OperationCanceledException and not WorkerException)
        {
            throw new WorkerException(e.Message, e);
        }
        finally
        {
            if (!cancelled)
            {
                End();
            }
        }
    }

    /// <summary>Awaits a worker call without a result. See <see cref="Complete{T}(Task{T}, CancellationToken)"/>.</summary>
    public Task Complete(Task task, CancellationToken cancellationToken) =>
        Complete(Wrap(task), cancellationToken);

    private static async Task<bool> Wrap(Task task)
    {
        await task.ConfigureAwait(false);
        return true;
    }

    internal static async Task Map(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException and not WorkerException)
        {
            throw new WorkerException(e.Message, e);
        }
    }

    internal void Cancel(Task running)
    {
        if (channel.TerminateOnCancel)
        {
            Js.Terminate(Handle);
            End();
            return;
        }

        Js.Cancel(Handle, Id);
        // The worker is still busy until the call stops; a pool lends it out again only then.
        _ = EndWhenSettled(running);
    }

    private async Task EndWhenSettled(Task running)
    {
        try
        {
            await running.ConfigureAwait(false);
        }
        catch
        {
            // Expected: the cancelled call rejects.
        }

        End();
    }

    internal void End()
    {
        if (ended)
        {
            return;
        }

        ended = true;
        Active.Remove(Id);
        channel.Release(slot);
    }

    internal void Track() => Active[Id] = this;

    // Staging: each Arg* call appends one argument for the next Invoke*.

    /// <summary>Stages an argument.</summary>
    public void ArgBool(bool value) => Js.ArgBool(value);

    /// <summary>Stages an argument.</summary>
    public void ArgI32(int value) => Js.ArgI32(value);

    /// <summary>Stages an argument.</summary>
    public void ArgF32(float value) => Js.ArgF32(value);

    /// <summary>Stages an argument.</summary>
    public void ArgF64(double value) => Js.ArgF64(value);

    /// <summary>Stages an argument.</summary>
    public void ArgStr(string? value) => Js.ArgStr(value);

    /// <summary>Stages an argument.</summary>
    public void ArgBytes(byte[]? value) => Js.ArgBytes(value);

    /// <summary>Stages an argument.</summary>
    public void ArgI32Array(int[]? value) => Js.ArgI32Array(value);

    /// <summary>Stages an argument.</summary>
    public void ArgF32Array(float[]? value) => Js.ArgF32Array(value);

    /// <summary>Stages an argument.</summary>
    public void ArgF64Array(double[]? value) => Js.ArgF64Array(value);

    // Invoking: calls worker export op with the call id and the staged arguments.

    /// <summary>Invokes the worker export <paramref name="op"/>.</summary>
    public Task InvokeVoid(string op) => Js.InvokeVoid(Handle, op, Id);

    /// <summary>Invokes the worker export <paramref name="op"/>.</summary>
    public Task<bool> InvokeBool(string op) => Js.InvokeBool(Handle, op, Id);

    /// <summary>Invokes the worker export <paramref name="op"/>.</summary>
    public Task<int> InvokeI32(string op) => Js.InvokeI32(Handle, op, Id);

    /// <summary>Invokes the worker export <paramref name="op"/>.</summary>
    public Task<float> InvokeF32(string op) => Js.InvokeF32(Handle, op, Id);

    /// <summary>Invokes the worker export <paramref name="op"/>.</summary>
    public Task<double> InvokeF64(string op) => Js.InvokeF64(Handle, op, Id);

    /// <summary>Invokes the worker export <paramref name="op"/>.</summary>
    public Task<string?> InvokeStr(string op) => Js.InvokeStr(Handle, op, Id);

    /// <summary>Invokes the worker export <paramref name="op"/>.</summary>
    public Task<byte[]?> InvokeBytes(string op) => Js.InvokeBytes(Handle, op, Id);

    /// <summary>Invokes the worker export <paramref name="op"/>.</summary>
    public Task<int[]?> InvokeI32Array(string op) => Js.InvokeI32Array(Handle, op, Id);

    /// <summary>Invokes the worker export <paramref name="op"/>.</summary>
    public Task<float[]?> InvokeF32Array(string op) => Js.InvokeF32Array(Handle, op, Id);

    /// <summary>Invokes the worker export <paramref name="op"/>.</summary>
    public Task<double[]?> InvokeF64Array(string op) => Js.InvokeF64Array(Handle, op, Id);

    // Taking: reads the value of the notification being dispatched (progress or a stream item).

    /// <summary>Reads the current notification's value.</summary>
    public static bool TakeBool() => Js.TakeBool();

    /// <summary>Reads the current notification's value.</summary>
    public static int TakeI32() => Js.TakeI32();

    /// <summary>Reads the current notification's value.</summary>
    public static float TakeF32() => Js.TakeF32();

    /// <summary>Reads the current notification's value.</summary>
    public static double TakeF64() => Js.TakeF64();

    /// <summary>Reads the current notification's value.</summary>
    public static string? TakeStr() => Js.TakeStr();

    /// <summary>Reads the current notification's value.</summary>
    public static byte[]? TakeBytes() => Js.TakeBytes();

    /// <summary>Reads the current notification's value.</summary>
    public static int[]? TakeI32Array() => Js.TakeI32Array();

    /// <summary>Reads the current notification's value.</summary>
    public static float[]? TakeF32Array() => Js.TakeF32Array();

    /// <summary>Reads the current notification's value.</summary>
    public static double[]? TakeF64Array() => Js.TakeF64Array();

    internal static class Js
    {
        // NetWasm binds one C# signature per (module, function), hence one JS name per type.
        private const string Module = "nwui.workers";

        [JSImport("create", Module)] public static extern int Create(string name);
        [JSImport("subscribe", Module)] public static extern JSSubscription Subscribe(Action<int, int> dispatch);
        [JSImport("cancel", Module)] public static extern void Cancel(int handle, int callId);
        [JSImport("credit", Module)] public static extern void Credit(int handle, int callId, int count);
        [JSImport("terminate", Module)] public static extern void Terminate(int handle);
        [JSImport("dispose", Module)] public static extern void Dispose(int handle);

        [JSImport("argBool", Module)] public static extern void ArgBool(bool value);
        [JSImport("argI32", Module)] public static extern void ArgI32(int value);
        [JSImport("argF32", Module)] public static extern void ArgF32(float value);
        [JSImport("argF64", Module)] public static extern void ArgF64(double value);
        [JSImport("argStr", Module)] public static extern void ArgStr(string? value);
        [JSImport("argBytes", Module)] public static extern void ArgBytes(byte[]? value);
        [JSImport("argI32Array", Module)] public static extern void ArgI32Array(int[]? value);
        [JSImport("argF32Array", Module)] public static extern void ArgF32Array(float[]? value);
        [JSImport("argF64Array", Module)] public static extern void ArgF64Array(double[]? value);

        [JSImport("invokeVoid", Module)] public static extern Task InvokeVoid(int handle, string op, int callId);
        [JSImport("invokeBool", Module)] public static extern Task<bool> InvokeBool(int handle, string op, int callId);
        [JSImport("invokeI32", Module)] public static extern Task<int> InvokeI32(int handle, string op, int callId);
        [JSImport("invokeF32", Module)] public static extern Task<float> InvokeF32(int handle, string op, int callId);
        [JSImport("invokeF64", Module)] public static extern Task<double> InvokeF64(int handle, string op, int callId);
        [JSImport("invokeStr", Module)] public static extern Task<string?> InvokeStr(int handle, string op, int callId);
        [JSImport("invokeBytes", Module)] public static extern Task<byte[]?> InvokeBytes(int handle, string op, int callId);
        [JSImport("invokeI32Array", Module)] public static extern Task<int[]?> InvokeI32Array(int handle, string op, int callId);
        [JSImport("invokeF32Array", Module)] public static extern Task<float[]?> InvokeF32Array(int handle, string op, int callId);
        [JSImport("invokeF64Array", Module)] public static extern Task<double[]?> InvokeF64Array(int handle, string op, int callId);

        [JSImport("takeBool", Module)] public static extern bool TakeBool();
        [JSImport("takeI32", Module)] public static extern int TakeI32();
        [JSImport("takeF32", Module)] public static extern float TakeF32();
        [JSImport("takeF64", Module)] public static extern double TakeF64();
        [JSImport("takeStr", Module)] public static extern string? TakeStr();
        [JSImport("takeBytes", Module)] public static extern byte[]? TakeBytes();
        [JSImport("takeI32Array", Module)] public static extern int[]? TakeI32Array();
        [JSImport("takeF32Array", Module)] public static extern float[]? TakeF32Array();
        [JSImport("takeF64Array", Module)] public static extern double[]? TakeF64Array();
    }
}
