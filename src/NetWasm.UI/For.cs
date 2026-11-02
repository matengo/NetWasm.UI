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
        // Skip the common prefix and suffix: they stay where they are. Appending, removing one row or
        // swapping two then only diffs the few keys in between.
        var oldCount = order.Count;
        var start = 0;
        while (start < oldCount && start < newOrder.Length && order[start].Equals(newOrder[start]))
        {
            start++;
        }

        var oldEnd = oldCount;
        var newEnd = newOrder.Length;
        while (oldEnd > start && newEnd > start && order[oldEnd - 1].Equals(newOrder[newEnd - 1]))
        {
            oldEnd--;
            newEnd--;
        }

        // Validate before touching anything. A middle key that exists but is not in the old middle is a
        // prefix/suffix key, i.e. a duplicate.
        Dictionary<object, int>? oldIndex = null;
        if (oldEnd > start)
        {
            oldIndex = new Dictionary<object, int>();
            for (var i = start; i < oldEnd; i++)
            {
                oldIndex[order[i]] = i;
            }
        }

        HashSet<object>? newKeys = newEnd - start > 1 || oldIndex is not null ? new HashSet<object>() : null;
        for (var i = start; i < newEnd; i++)
        {
            var k = newOrder[i];
            if ((newKeys is not null && !newKeys.Add(k)) || (entries.ContainsKey(k) && (oldIndex is null || !oldIndex.ContainsKey(k))))
            {
                throw DuplicateKey(k);
            }
        }

        // Remove entries of the old middle that disappeared.
        if (oldIndex is not null)
        {
            for (var i = start; i < oldEnd; i++)
            {
                var k = order[i];
                if (!newKeys!.Contains(k))
                {
                    var entry = entries[k];
                    entry.Node.Remove();
                    entry.Scope.Dispose();
                    entries.Remove(k);
                }
            }
        }

        // Surviving items that form the longest increasing subsequence of old positions keep their
        // relative order and are never moved; everything else is (re)inserted before its successor.
        var sequence = new int[newEnd - start];
        var anySurvivor = false;
        for (var i = 0; i < sequence.Length; i++)
        {
            if (oldIndex is not null && oldIndex.TryGetValue(newOrder[start + i], out var idx))
            {
                sequence[i] = idx;
                anySurvivor = true;
            }
            else
            {
                sequence[i] = -1;
            }
        }

        var stable = anySurvivor ? LongestIncreasingSubsequence(sequence) : null;

        // Walk the middle backwards so the reference node is always already in position.
        var referenceId = newEnd < newOrder.Length ? entries[newOrder[newEnd]].Node.Id : anchor.Id;
        for (var i = newEnd - 1; i >= start; i--)
        {
            var k = newOrder[i];
            int nodeId;

            if (sequence[i - start] >= 0)
            {
                nodeId = entries[k].Node.Id;
                if (!stable![i - start])
                {
                    Renderer.InsertBefore(referenceId, nodeId);
                }
            }
            else
            {
                var scope = Scope.Detached(owner);
                var previousOwner = Tracker.CurrentOwner;
                Tracker.CurrentOwner = scope;
                Node node;
                try
                {
                    node = render(i);
                }
                finally
                {
                    Tracker.CurrentOwner = previousOwner;
                }

                entries[k] = new Entry(node, scope);
                nodeId = node.Id;
                Renderer.InsertBefore(referenceId, nodeId);
            }

            referenceId = nodeId;
        }

        order.Clear();
        order.AddRange(newOrder);
    }

    private static InvalidOperationException DuplicateKey(object key) =>
        new("Duplicate key in For: " + key);

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