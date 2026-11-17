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
                // compiled once per instantiation (closed-world AOT doesn't share generic code). They go into a
                // buffer the list keeps, and a key that is where it was, counted from the start or from the end,
                // keeps its boxed object: appending, removing or swapping rows then allocates a key only for the
                // rows that changed, and unchanged keys compare by reference.
                var count = list.Count;
                var keys = keyed.KeyBuffer(count);
                var oldCount = keyed.Count;
                var fromEnd = oldCount - count;
                for (var i = 0; i < count; i++)
                {
                    var k = key(list[i]);
                    if (i < oldCount && keyed.KeyAt(i) is TKey same && EqualityComparer<TKey>.Default.Equals(same, k))
                    {
                        keys[i] = keyed.KeyAt(i);
                    }
                    else if (fromEnd != 0 && i + fromEnd >= 0 && keyed.KeyAt(i + fromEnd) is TKey shifted &&
                        EqualityComparer<TKey>.Default.Equals(shifted, k))
                    {
                        keys[i] = keyed.KeyAt(i + fromEnd);
                    }
                    else
                    {
                        keys[i] = k;
                    }
                }

                keyed.Reconcile(keys, count, i => render(list[i]));
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
    // The keys in list order, and the buffer the next list's keys are written into. They change places after each
    // reconcile, so a list change allocates neither.
    private object[] order = Array.Empty<object>();
    private object[] spare = Array.Empty<object>();
    private int count;
    // The items' nodes, in the same order as their keys.
    private readonly List<Node> nodes = new();
    // What the middle of a diff needs, kept from one reconcile to the next and emptied after each.
    private Dictionary<object, int>? oldIndex;
    private HashSet<object>? newKeys;
    private List<int>? tails;
    private int[] sequence = Array.Empty<int>();
    private int[] predecessor = Array.Empty<int>();
    private bool[] stable = Array.Empty<bool>();
    private Node[] middle = Array.Empty<Node>();

    public KeyedList(Element parent, Scope? owner)
    {
        this.owner = owner;
        Renderer.AppendChild(parent.Id, anchor.Id);
        // Item scopes are detached from the owner (so removed items don't pile up in it); the list
        // disposes whatever items are left when the owner goes away.
        owner?.Register(this);
    }

    internal int Count => count;

    internal object KeyAt(int index) => order[index];

    /// <summary>The buffer for the next list's keys; pass it to <see cref="Reconcile"/>.</summary>
    internal object[] KeyBuffer(int size)
    {
        if (spare.Length < size)
        {
            spare = new object[Math.Max(size, spare.Length * 2)];
        }

        return spare;
    }

    public void Dispose()
    {
        for (var i = count - 1; i >= 0; i--)
        {
            if (entries.TryGetValue(order[i], out var entry))
            {
                entry.Scope.Dispose();
            }
        }

        entries.Clear();
        Array.Clear(order, 0, count);
        count = 0;
        nodes.Clear();
    }

    // Most keys of a new list are the very objects of the old one (see For), so try the reference first.
    private static bool Same(object left, object right) => ReferenceEquals(left, right) || left.Equals(right);

    // The new keys become the order, and the old order becomes the buffer for the next list.
    private void Adopt(object[] newOrder, int newCount)
    {
        Array.Clear(order, 0, count);
        spare = order;
        order = newOrder;
        count = newCount;
    }

    public void Reconcile(object[] newOrder, int newCount, Func<int, Node> render)
    {
        var oldCount = count;

        // An empty list: detach every row with one op and dispose them; nothing to diff.
        if (newCount == 0)
        {
            if (oldCount > 1)
            {
                Renderer.RemoveRange(nodes[0].Id, anchor.Id);
            }
            else if (oldCount == 1)
            {
                nodes[0].Remove();
            }

            for (var i = 0; i < oldCount; i++)
            {
                entries[order[i]].Scope.Dispose();
            }

            entries.Clear();
            nodes.Clear();
            Adopt(newOrder, 0);
            return;
        }

        // Skip the common prefix and suffix: they stay where they are. Appending, removing one row or
        // swapping two then only diffs the few keys in between.
        var start = 0;
        while (start < oldCount && start < newCount && Same(order[start], newOrder[start]))
        {
            start++;
        }

        var oldEnd = oldCount;
        var newEnd = newCount;
        while (oldEnd > start && newEnd > start && Same(order[oldEnd - 1], newOrder[newEnd - 1]))
        {
            oldEnd--;
            newEnd--;
        }

        // Two rows changed places and everything between them stayed: move the two, with no lookup tables.
        var length = oldEnd - start;
        if (length >= 2 && newEnd - start == length &&
            Same(order[start], newOrder[newEnd - 1]) && Same(order[oldEnd - 1], newOrder[start]))
        {
            var between = start + 1;
            while (between < oldEnd - 1 && Same(order[between], newOrder[between]))
            {
                between++;
            }

            if (between == oldEnd - 1)
            {
                var first = nodes[start];
                var last = nodes[oldEnd - 1];
                Renderer.InsertBefore(length > 2 ? nodes[start + 1].Id : first.Id, last.Id);
                if (length > 2)
                {
                    Renderer.InsertBefore(oldEnd < oldCount ? nodes[oldEnd].Id : anchor.Id, first.Id);
                }

                nodes[start] = last;
                nodes[oldEnd - 1] = first;
                Adopt(newOrder, newCount);
                return;
            }
        }

        var size = newEnd - start;
        Dictionary<object, int>? index = null;
        HashSet<object>? fresh = null;
        try
        {
            // Validate before touching anything. A middle key that exists but is not in the old middle is a
            // prefix/suffix key, i.e. a duplicate.
            if (oldEnd > start)
            {
                index = oldIndex ??= new Dictionary<object, int>();
                for (var i = start; i < oldEnd; i++)
                {
                    index[order[i]] = i;
                }
            }

            fresh = size > 1 || index is not null ? newKeys ??= new HashSet<object>() : null;
            for (var i = start; i < newEnd; i++)
            {
                var k = newOrder[i];
                if ((fresh is not null && !fresh.Add(k)) || (entries.ContainsKey(k) && (index is null || !index.ContainsKey(k))))
                {
                    throw DuplicateKey(k);
                }
            }

            // Remove entries of the old middle that disappeared. Each item is one node and they sit in order
            // before the next item (or the anchor), so when none of the old middle survives (replace)
            // one range op detaches them all.
            if (index is not null)
            {
                var bulk = oldEnd - start > 1;
                for (var i = start; bulk && i < oldEnd; i++)
                {
                    bulk = !fresh!.Contains(order[i]);
                }

                if (bulk)
                {
                    var endId = oldEnd < oldCount ? nodes[oldEnd].Id : anchor.Id;
                    Renderer.RemoveRange(nodes[start].Id, endId);
                }

                for (var i = start; i < oldEnd; i++)
                {
                    var k = order[i];
                    if (bulk || !fresh!.Contains(k))
                    {
                        var entry = entries[k];
                        if (!bulk)
                        {
                            entry.Node.Remove();
                        }

                        entry.Scope.Dispose();
                        entries.Remove(k);
                    }
                }
            }

            // Surviving items that form the longest increasing subsequence of old positions keep their
            // relative order and are never moved; everything else is (re)inserted before its successor.
            if (sequence.Length < size)
            {
                sequence = new int[size];
                predecessor = new int[size];
                stable = new bool[size];
                middle = new Node[size];
            }

            var anySurvivor = false;
            for (var i = 0; i < size; i++)
            {
                if (index is not null && index.TryGetValue(newOrder[start + i], out var idx))
                {
                    sequence[i] = idx;
                    anySurvivor = true;
                }
                else
                {
                    sequence[i] = -1;
                }
            }

            if (anySurvivor)
            {
                LongestIncreasingSubsequence(size);
            }

            // Walk the middle backwards so the reference node is always already in position.
            var referenceId = oldEnd < oldCount ? nodes[oldEnd].Id : anchor.Id;
            for (var i = newEnd - 1; i >= start; i--)
            {
                var k = newOrder[i];
                int nodeId;

                if (sequence[i - start] >= 0)
                {
                    middle[i - start] = entries[k].Node;
                    nodeId = middle[i - start].Id;
                    if (!stable[i - start])
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
                    middle[i - start] = node;
                    nodeId = node.Id;
                    Renderer.InsertBefore(referenceId, nodeId);
                }

                referenceId = nodeId;
            }

            if (size == oldEnd - start)
            {
                for (var i = 0; i < size; i++)
                {
                    nodes[start + i] = middle[i];
                }
            }
            else
            {
                nodes.RemoveRange(start, oldEnd - start);
                nodes.InsertRange(start, new ArraySegment<Node>(middle, 0, size));
            }

            Adopt(newOrder, newCount);
        }
        finally
        {
            index?.Clear();
            fresh?.Clear();
            Array.Clear(middle, 0, Math.Min(size, middle.Length));
        }
    }

    private static InvalidOperationException DuplicateKey(object key) =>
        new("Duplicate key in For: " + key);

    /// <summary>Marks positions belonging to a longest increasing subsequence of non-negative values.</summary>
    // Marks in stable the items of sequence[0..size) that form its longest increasing subsequence.
    private void LongestIncreasingSubsequence(int size)
    {
        var tail = tails ??= new List<int>(); // indices into sequence
        tail.Clear();
        Array.Clear(stable, 0, size);

        for (var i = 0; i < size; i++)
        {
            if (sequence[i] < 0)
            {
                continue;
            }

            var lo = 0;
            var hi = tail.Count;
            while (lo < hi)
            {
                var mid = (lo + hi) >> 1;
                if (sequence[tail[mid]] < sequence[i])
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            predecessor[i] = lo > 0 ? tail[lo - 1] : -1;
            if (lo == tail.Count)
            {
                tail.Add(i);
            }
            else
            {
                tail[lo] = i;
            }
        }

        if (tail.Count > 0)
        {
            for (var idx = tail[^1]; idx >= 0; idx = predecessor[idx])
            {
                stable[idx] = true;
            }
        }
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