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
        var keyed = new KeyedList(parent, Scope.Current);

        _ = new Effect(() =>
        {
            var list = items();
            Tracker.Untracked(() =>
            {
                // Keys are boxed so the diffing below is shared by every For<T, TKey> instead of being
                // compiled once per instantiation (closed-world AOT doesn't share generic code).
                var keys = new object[list.Count];
                for (var i = 0; i < keys.Length; i++)
                {
                    keys[i] = key(list[i]);
                }

                keyed.Reconcile(keys, i => render(list[i]));
            });
        });
    }
}

/// <summary>The non-generic part of <see cref="For{T, TKey}"/>: diffs boxed keys and moves DOM nodes.</summary>
internal sealed class KeyedList : IDisposable
{
    private readonly Anchor anchor = new();
    private readonly Scope? owner;
    private readonly Dictionary<object, Entry> entries = new();
    private readonly List<object> order = new();

    public KeyedList(Element parent, Scope? owner)
    {
        this.owner = owner;
        Renderer.AppendChild(parent.Id, anchor.Id);
        // Item scopes are detached from the owner (so removed items don't pile up in it); the list
        // disposes whatever items are left when the owner goes away.
        owner?.Register(this);
    }

    public void Dispose()
    {
        for (var i = order.Count - 1; i >= 0; i--)
        {
            if (entries.TryGetValue(order[i], out var entry))
            {
                entry.Scope.Dispose();
            }
        }

        entries.Clear();
        order.Clear();
    }

    public void Reconcile(object[] newOrder, Func<int, Node> render)
    {
        var newKeys = new HashSet<object>();
        for (var i = 0; i < newOrder.Length; i++)
        {
            if (!newKeys.Add(newOrder[i]))
            {
                throw new InvalidOperationException("Duplicate key in For: " + newOrder[i]);
            }
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
        var oldIndex = new Dictionary<object, int>();
        for (var i = 0; i < order.Count; i++)
        {
            oldIndex[order[i]] = i;
        }

        var sequence = new int[newOrder.Length];
        for (var i = 0; i < newOrder.Length; i++)
        {
            sequence[i] = oldIndex.TryGetValue(newOrder[i], out var idx) ? idx : -1;
        }

        var stable = LongestIncreasingSubsequence(sequence);

        // Walk the new order backwards so the reference node is always already in position.
        var referenceId = anchor.Id;
        for (var i = newOrder.Length - 1; i >= 0; i--)
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
                var scope = Scope.Detached(owner);
                var index = i;
                var node = scope.Run(() => render(index));
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