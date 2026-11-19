using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices.JavaScript;
using System.Threading;
using System.Threading.Tasks;

namespace NetWasm.UI.Workers;

/// <summary>Worker side of calls from the app: cancellation, progress and streaming. Used by generated exports.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class WorkerCalls
{
    private const int InitialCredits = 16;

    private static readonly Dictionary<int, Call> Calls = new();

    /// <summary>Registers call <paramref name="callId"/>; its token is cancelled by <see cref="Cancel"/>.</summary>
    public static CancellationToken Begin(int callId)
    {
        var call = new Call();
        Calls[callId] = call;
        return call.Cancellation.Token;
    }

    /// <summary>Forgets call <paramref name="callId"/>.</summary>
    public static void End(int callId)
    {
        if (Calls.TryGetValue(callId, out var call))
        {
            Calls.Remove(callId);
            call.Cancellation.Dispose();
        }
    }

    /// <summary>Cancels call <paramref name="callId"/>, if it is still running.</summary>
    public static void Cancel(int callId)
    {
        if (Calls.TryGetValue(callId, out var call))
        {
            call.Cancellation.Cancel();
            call.Waiter?.TrySetResult(true);
        }
    }

    /// <summary>Lets call <paramref name="callId"/> send <paramref name="count"/> more stream items.</summary>
    public static void Credit(int callId, int count)
    {
        if (Calls.TryGetValue(callId, out var call))
        {
            call.Credits += count;
            call.Waiter?.TrySetResult(true);
        }
    }

    /// <summary>A progress reporter that notifies the app, or null when the app passed none.</summary>
    public static IProgress<T>? Progress<T>(int callId, bool enabled, Action<int, int, T> notify) =>
        enabled ? new Reporter<T>(callId, notify) : null;

    /// <summary>Sends the items of <paramref name="items"/> to the app, as fast as the app takes them.</summary>
    public static async Task Stream<T>(int callId, IAsyncEnumerable<T> items, Action<int, int, T> notify)
    {
        if (!Calls.TryGetValue(callId, out var call))
        {
            throw new InvalidOperationException("Call " + callId.ToString() + " was not started.");
        }

        var token = call.Cancellation.Token;
        await foreach (var item in items.WithCancellation(token).ConfigureAwait(false))
        {
            while (call.Credits == 0)
            {
                token.ThrowIfCancellationRequested();
                call.Waiter = new TaskCompletionSource<bool>();
                await call.Waiter.Task.ConfigureAwait(false);
                call.Waiter = null;
            }

            token.ThrowIfCancellationRequested();
            call.Credits--;
            notify(callId, 1, item);
        }
    }

    /// <summary>Sends a notification value.</summary>
    public static void NotifyBool(int callId, int channel, bool value) => Js.NotifyBool(callId, channel, value);

    /// <summary>Sends a notification value.</summary>
    public static void NotifyI32(int callId, int channel, int value) => Js.NotifyI32(callId, channel, value);

    /// <summary>Sends a notification value.</summary>
    public static void NotifyF32(int callId, int channel, float value) => Js.NotifyF32(callId, channel, value);

    /// <summary>Sends a notification value.</summary>
    public static void NotifyF64(int callId, int channel, double value) => Js.NotifyF64(callId, channel, value);

    /// <summary>Sends a notification value.</summary>
    public static void NotifyStr(int callId, int channel, string? value) => Js.NotifyStr(callId, channel, value);

    /// <summary>Sends a notification value.</summary>
    public static void NotifyBytes(int callId, int channel, byte[]? value) => Js.NotifyBytes(callId, channel, value);

    /// <summary>Sends a notification value.</summary>
    public static void NotifyI32Array(int callId, int channel, int[]? value) => Js.NotifyI32Array(callId, channel, value);

    /// <summary>Sends a notification value.</summary>
    public static void NotifyF32Array(int callId, int channel, float[]? value) => Js.NotifyF32Array(callId, channel, value);

    /// <summary>Sends a notification value.</summary>
    public static void NotifyF64Array(int callId, int channel, double[]? value) => Js.NotifyF64Array(callId, channel, value);

    private sealed class Call
    {
        public readonly CancellationTokenSource Cancellation = new();
        public int Credits = InitialCredits;
        public TaskCompletionSource<bool>? Waiter;
    }

    private sealed class Reporter<T> : IProgress<T>
    {
        private readonly int callId;
        private readonly Action<int, int, T> notify;

        public Reporter(int callId, Action<int, int, T> notify)
        {
            this.callId = callId;
            this.notify = notify;
        }

        public void Report(T value) => notify(callId, 0, value);
    }

    private static class Js
    {
        private const string Module = "nwui.workers.worker";

        [JSImport("notifyBool", Module)] public static extern void NotifyBool(int callId, int channel, bool value);
        [JSImport("notifyI32", Module)] public static extern void NotifyI32(int callId, int channel, int value);
        [JSImport("notifyF32", Module)] public static extern void NotifyF32(int callId, int channel, float value);
        [JSImport("notifyF64", Module)] public static extern void NotifyF64(int callId, int channel, double value);
        [JSImport("notifyStr", Module)] public static extern void NotifyStr(int callId, int channel, string? value);
        [JSImport("notifyBytes", Module)] public static extern void NotifyBytes(int callId, int channel, byte[]? value);
        [JSImport("notifyI32Array", Module)] public static extern void NotifyI32Array(int callId, int channel, int[]? value);
        [JSImport("notifyF32Array", Module)] public static extern void NotifyF32Array(int callId, int channel, float[]? value);
        [JSImport("notifyF64Array", Module)] public static extern void NotifyF64Array(int callId, int channel, double[]? value);
    }
}
