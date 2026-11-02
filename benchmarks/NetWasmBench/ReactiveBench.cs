using System;
using System.Collections.Generic;
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
        "sort" => Sort(),
        "sieve" => Sieve(),
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

    private sealed class Item
    {
        public Item(int id, string label)
        {
            Id = id;
            Label = label;
        }

        public int Id { get; }

        public string Label { get; }
    }

    /// <summary>No reactivity: builds 100,000 labelled items and sorts them by label (ordinal), then id.</summary>
    private static int Sort()
    {
        var state = 1;
        var items = new List<Item>(100_000);
        for (var i = 0; i < 100_000; i++)
        {
            items.Add(new Item(i, Data.Label(ref state)));
        }

        items.Sort(static (a, b) =>
        {
            var c = string.CompareOrdinal(a.Label, b.Label);
            return c != 0 ? (c < 0 ? -1 : 1) : a.Id - b.Id;
        });

        var hash = 0;
        for (var i = 0; i < items.Count; i++)
        {
            hash = unchecked(hash * 31 + items[i].Id);
        }

        return hash;
    }

    /// <summary>No reactivity: counts the primes below 2,000,000 with a sieve of Eratosthenes.</summary>
    private static int Sieve()
    {
        const int n = 2_000_000;
        var composite = new bool[n];
        var count = 0;
        for (var i = 2; i < n; i++)
        {
            if (composite[i])
            {
                continue;
            }

            count++;
            for (long j = (long)i * i; j < n; j += i)
            {
                composite[j] = true;
            }
        }

        return count;
    }
}
