using System;
using System.Collections.Generic;
using NetWasm.UI.Dom;
using NetWasm.UI.Reactive;

namespace NetWasm.UI;

/// <summary>
/// Keyed list rendering. Each item is built once inside its own scope. When the list changes,
/// removed keys are disposed, new keys are built, and only items whose relative order changed
/// are moved.
/// </summary>
public sealed class For<T, TKey> : Fragment
    where TKey : notnull
{
    private readonly Func<IReadOnlyList<T>> items;
    private readonly Func<T, TKey> key;
    private readonly Func<T, Node> render;

    public For(Func<IReadOnlyList<T>> items, Func<T, TKey> key, Func<T, Node> render)
    {
        this.items = items;
        this.key = key;
        this.render = render;
    }

    protected override void Apply(Element parent)
    {
        var owner = Scope.Current;
        var anchor = new Anchor();
        Renderer.AppendChild(parent.Id, anchor.Id);

        var state = new State(anchor, owner);

        _ = new Effect(() =>
        {
            var list = items();
            Tracker.Untracked(() => Reconcile(list, state));
        });
    }

    private void Reconcile(IReadOnlyList<T> list, State state)
    {
        var entries = state.Entries;
        var order = state.Order;
        var comparer = EqualityComparer<TKey>.Default;

        var newOrder = new List<TKey>(list.Count);
        var newKeys = new HashSet<TKey>(comparer);
        for (var i = 0; i < list.Count; i++)
        {
            var k = key(list[i]);
            if (!newKeys.Add(k))
            {
                throw new InvalidOperationException($"Duplicate key in For: {k}");
            }

            newOrder.Add(k);
        }

        // Remove entries that disappeared.
        for (var i = 0; i < order.Count; i++)
        {
            var k = order[i];
            if (!newKeys.Contains(k))
            {
                var entry = entries[k];
                entry.Node.Remove();
                entry.Scope.Dispose();
                entries.Remove(k);
            }
        }

        // Surviving items that form the longest increasing subsequence of old positions keep their
        // relative order and are never moved; everything else is (re)inserted before its successor.
        var oldIndex = new Dictionary<TKey, int>(comparer);
        for (var i = 0; i < order.Count; i++)
        {
            oldIndex[order[i]] = i;
        }

        var sequence = new int[newOrder.Count];
        for (var i = 0; i < newOrder.Count; i++)
        {
            sequence[i] = oldIndex.TryGetValue(newOrder[i], out var idx) ? idx : -1;
        }

        var stable = LongestIncreasingSubsequence(sequence);

        // Walk the new order backwards so the reference node is always already in position.
        var referenceId = state.Anchor.Id;
        for (var i = newOrder.Count - 1; i >= 0; i--)
        {
            var k = newOrder[i];
            int nodeId;

            if (entries.TryGetValue(k, out var existing))
            {
                nodeId = existing.Node.Id;
                if (!stable[i])
                {
                    Renderer.InsertBefore(referenceId, nodeId);
                }
            }
            else
            {
                var scope = new Scope(state.Owner);
                var item = list[i];
                var node = scope.Run(() => render(item));
                entries[k] = new Entry(node, scope);
                nodeId = node.Id;
                Renderer.InsertBefore(referenceId, nodeId);
            }

            referenceId = nodeId;
        }

        order.Clear();
        order.AddRange(newOrder);
    }

    /// <summary>Marks positions belonging to a longest increasing subsequence of non-negative values.</summary>
    private static bool[] LongestIncreasingSubsequence(int[] sequence)
    {
        var n = sequence.Length;
        var result = new bool[n];
        var predecessor = new int[n];
        var tails = new List<int>(); // indices into sequence

        for (var i = 0; i < n; i++)
        {
            if (sequence[i] < 0)
            {
                continue;
            }

            var lo = 0;
            var hi = tails.Count;
            while (lo < hi)
            {
                var mid = (lo + hi) >> 1;
                if (sequence[tails[mid]] < sequence[i])
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            predecessor[i] = lo > 0 ? tails[lo - 1] : -1;
            if (lo == tails.Count)
            {
                tails.Add(i);
            }
            else
            {
                tails[lo] = i;
            }
        }

        if (tails.Count > 0)
        {
            for (var idx = tails[^1]; idx >= 0; idx = predecessor[idx])
            {
                result[idx] = true;
            }
        }

        return result;
    }

    private sealed class State
    {
        public State(Anchor anchor, Scope? owner)
        {
            Anchor = anchor;
            Owner = owner;
        }

        public Anchor Anchor { get; }
        public Scope? Owner { get; }
        public Dictionary<TKey, Entry> Entries { get; } = new();
        public List<TKey> Order { get; } = new();
    }

    private readonly struct Entry
    {
        public Entry(Node node, Scope scope)
        {
            Node = node;
            Scope = scope;
        }

        public Node Node { get; }
        public Scope Scope { get; }
    }
}
