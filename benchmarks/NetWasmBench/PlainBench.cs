using System.Collections.Generic;

namespace NetWasmBench;

/// <summary>Workloads without reactivity or DOM. Also linked into BlazorBench to compare the C# compilers.</summary>
public static class PlainBench
{
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

    /// <summary>Builds 100,000 labelled items and sorts them by label (ordinal), then id.</summary>
    public static int Sort()
    {
        var state = 1;
        var items = new List<Item>(100_000);
        for (var i = 0; i < 100_000; i++)
        {
            items.Add(new Item(i, Labels.Label(ref state)));
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

    /// <summary>Counts the primes below 2,000,000 with a sieve of Eratosthenes.</summary>
    public static int Sieve()
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
