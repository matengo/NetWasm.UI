using System;
using System.Diagnostics;
using System.Globalization;
using NetWasm.UI;
using NetWasm.UI.Dom;
using NetWasm.UI.Reactive;

namespace NetWasmBench;

/// <summary>
/// Times the C# side of the keyed benchmark's cases under Wasmtime: the app renders into a host that only
/// counts the bytes of each batch, so no browser, DOM or JavaScript takes part.
/// </summary>
/// <remarks>
/// <c>wasmtime run WasmBench.wasm [case filter] [runs]</c>. Each line is: case, median and fastest time in
/// microseconds over the runs, time of the first run, then per run: bytes allocated, collections and bytes of
/// render batch. Every run starts right after a collection, so the collections inside it fall in the same place
/// each time and the allocation and batch figures are exact.
/// </remarks>
public static class Program
{
    private static readonly DiscardingHost Host = new();
    private static BenchApp app = null!;
    private static string filter = string.Empty;
    private static int runs = 25;

    public static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            filter = args[0];
        }

        if (args.Length > 1)
        {
            runs = int.Parse(args[1], CultureInfo.InvariantCulture);
        }

        Renderer.Initialize(Host);
        app = new BenchApp();
        App.Mount(app, "#main");

        // The same cases as benchmarks/Runner drives in the browser, each measured from the state its
        // preparation there leaves behind (a filled table, five earlier updates, swaps or removals).
        Case("create 1,000 rows", () => Do(app.Clear), () => app.Run(1000));
        Case("replace 1,000 rows", () => Rows(1000), () => app.Run(1000));
        Case("partial update (every 10th)", () => Rows(1000, app.UpdateEveryTenth), () => app.UpdateEveryTenth());
        Case("select row", () => Rows(1000), () => app.SelectAt(1));
        Case("swap rows", () => Rows(1000, app.SwapRows), () => app.SwapRows());
        Case("remove row", () => Rows(1000, () => app.RemoveAt(3)), () => app.RemoveAt(3));
        Case("create 10,000 rows", () => Do(app.Clear), () => app.Run(10000));
        Case("append 1,000 rows", () => Rows(1000), () => app.Append());
        Case("clear 1,000 rows", () => Rows(1000), () => app.Clear());
        return 0;
    }

    // One user action: its effects run and its render batch is flushed before the next one.
    private static void Do(Action action) => Scheduler.Batch(action);

    // Leaves the table with this many fresh rows, then repeats an action five times as the Runner's preparation does.
    private static void Rows(int count, Action? fiveTimes = null)
    {
        Do(app.Clear);
        Do(() => app.Run(count));
        for (var i = 0; fiveTimes is not null && i < 5; i++)
        {
            Do(fiveTimes);
        }
    }

    private static void Case(string name, Action prepare, Action measured)
    {
        if (name.IndexOf(filter, StringComparison.Ordinal) < 0)
        {
            return;
        }

        var times = new long[runs];
        long allocated = 0;
        long batch = 0;
        var collections = 0;
        for (var i = 0; i < runs; i++)
        {
            prepare();
            GC.Collect();
            var bytesBefore = GC.GetTotalAllocatedBytes();
            var collectionsBefore = GC.CollectionCount(0);
            var batchBefore = Host.Bytes;
            var start = Stopwatch.GetTimestamp();
            Do(measured);
            times[i] = Stopwatch.GetTimestamp() - start;
            allocated = GC.GetTotalAllocatedBytes() - bytesBefore;
            collections = GC.CollectionCount(0) - collectionsBefore;
            batch = Host.Bytes - batchBefore;
        }

        var first = times[0];
        Array.Sort(times);
        Console.WriteLine(
            name + "\t" + Microseconds(times[runs / 2]) + "\t" + Microseconds(times[0]) + "\t" + Microseconds(first) +
            "\t" + allocated.ToString(CultureInfo.InvariantCulture) + "\t" + collections.ToString(CultureInfo.InvariantCulture) +
            "\t" + batch.ToString(CultureInfo.InvariantCulture));
    }

    private static string Microseconds(long ticks) =>
        (ticks * 1_000_000 / Stopwatch.Frequency).ToString(CultureInfo.InvariantCulture);

    private sealed class DiscardingHost : IDomHost
    {
        public long Bytes { get; private set; }

        public void ApplyBatch(ReadOnlySpan<byte> ops) => Bytes += ops.Length;
    }
}
