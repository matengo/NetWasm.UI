using System;
using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;
using System.Threading;
using System.Threading.Tasks;

namespace NetWasm.UI.Workers;

/// <summary>Helpers for code that runs in a worker, and worker settings for the app.</summary>
public static class Worker
{
    private static long lastYield;

    /// <summary>
    /// The pool size <c>AddWorkerPool&lt;T&gt;()</c> uses by default: one worker per logical processor, leaving one for the
    /// page (<c>navigator.hardwareConcurrency - 1</c>, at least 1). App side only.
    /// </summary>
    public static int DefaultPoolSize => Math.Max(1, WorkerJs.HardwareConcurrency() - 1);

    /// <summary>
    /// A progress reporter for worker calls that calls <paramref name="report"/> directly as each report arrives.
    /// (<see cref="System.Progress{T}"/> posts to a synchronization context instead.)
    /// </summary>
    public static IProgress<T> Progress<T>(Action<T> report) => new Reporter<T>(report ?? throw new ArgumentNullException(nameof(report)));

    /// <summary>
    /// Call this in long computations in a worker. About every 8 ms it lets the worker's event loop run, so a cancel
    /// from the app reaches <paramref name="cancellationToken"/>, and then throws if it was cancelled; in between it
    /// returns at once. <c>await Task.Yield()</c> does not do this: it continues without returning to JavaScript.
    /// Worker side only.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public static ValueTask YieldAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = Stopwatch.GetTimestamp();
        if (now - lastYield < Stopwatch.Frequency / 125)
        {
            return default;
        }

        return Slow(cancellationToken);

        static async ValueTask Slow(CancellationToken cancellationToken)
        {
            await WorkerSideJs.YieldToHost().ConfigureAwait(false);
            lastYield = Stopwatch.GetTimestamp();
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private sealed class Reporter<T> : IProgress<T>
    {
        private readonly Action<T> report;

        public Reporter(Action<T> report) => this.report = report;

        public void Report(T value) => report(value);
    }
}

internal static class WorkerJs
{
    private const string Module = "nwui.workers";

    [JSImport("hardwareConcurrency", Module)]
    public static extern int HardwareConcurrency();
}

internal static class WorkerSideJs
{
    private const string Module = "nwui.workers.worker";

    [JSImport("yieldToHost", Module)]
    public static extern Task YieldToHost();
}
