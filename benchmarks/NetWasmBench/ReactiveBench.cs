using System;
using NetWasm.UI.Reactive;

namespace NetWasmBench;

/// <summary>
/// DOM-free workloads. <c>benchmarks/solid/src/reactive.js</c> implements each one identically with
/// Solid primitives (or plain JS), and the runner checks that both return the same checksum.
/// </summary>
public static class ReactiveBench
{
    public static int Run(string name) => name switch
    {
        "deep" => Deep(),
        "broad" => Broad(),
        "diamond" => Diamond(),
        "create" => Create(),
        "sort" => PlainBench.Sort(),
        "sieve" => PlainBench.Sieve(),
        _ => throw new ArgumentException("Unknown workload: " + name),
    };

    /// <summary>A chain of 100 computeds behind one signal, updated 1,000 times.</summary>
    private static int Deep()
    {
        var sum = 0;
        var root = new Scope(null);
        var source = new Signal<int>(0);
        root.Run(() =>
        {
            IReadOnlySignal<int> current = source;
            for (var i = 0; i < 100; i++)
            {
                var previous = current;
                current = new Computed<int>(() => previous.Value + 1);
            }

            var last = current;
            _ = new Effect(() => sum += last.Value);
        });

        for (var i = 1; i <= 1000; i++)
        {
            source.Value = i;
        }

        root.Dispose();
        return sum;
    }

    /// <summary>1,000 computeds (each with an effect) reading one signal, updated 100 times.</summary>
    private static int Broad()
    {
        var sum = 0;
        var root = new Scope(null);
        var source = new Signal<int>(0);
        root.Run(() =>
        {
            for (var i = 0; i < 1000; i++)
            {
                var k = i;
                var c = new Computed<int>(() => source.Value + k);
                _ = new Effect(() => sum += c.Value);
            }
        });

        for (var i = 1; i <= 100; i++)
        {
            source.Value = i;
        }

        root.Dispose();
        return sum;
    }

    /// <summary>One signal fanning out to 10 computeds that join in one computed, updated 1,000 times.</summary>
    private static int Diamond()
    {
        var sum = 0;
        var root = new Scope(null);
        var source = new Signal<int>(0);
        root.Run(() =>
        {
            var branches = new Computed<int>[10];
            for (var i = 0; i < branches.Length; i++)
            {
                var k = i + 1;
                branches[i] = new Computed<int>(() => source.Value * k);
            }

            var total = new Computed<int>(() =>
            {
                var t = 0;
                for (var i = 0; i < branches.Length; i++)
                {
                    t += branches[i].Value;
                }

                return t;
            });
            _ = new Effect(() => sum += total.Value);
        });

        for (var i = 1; i <= 1000; i++)
        {
            source.Value = i;
        }

        root.Dispose();
        return sum;
    }

    /// <summary>Creates 10,000 signal + computed + effect triples in a scope, updates each once, disposes them.</summary>
    private static int Create()
    {
        var sum = 0;
        var root = new Scope(null);
        var signals = new Signal<int>[10_000];
        root.Run(() =>
        {
            for (var i = 0; i < signals.Length; i++)
            {
                var s = new Signal<int>(i);
                var c = new Computed<int>(() => s.Value * 2);
                _ = new Effect(() => sum += c.Value);
                signals[i] = s;
            }
        });

        for (var i = 0; i < signals.Length; i++)
        {
            signals[i].Value = i + 1;
        }

        root.Dispose();
        return sum;
    }
}
