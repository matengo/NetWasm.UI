using System;
using System.Collections.Generic;
using NetWasm.UI.Dom;
using NetWasm.UI.Reactive;

namespace NetWasm.UI;

/// <summary>
/// Keyed list rendering. Each item is built once inside its own scope. When the list changes,
/// removed keys are disposed, new keys are built, and only items whose relative order changed
/// are moved. A key that stays but has a different item (another object, or for a value type a
/// value that is not equal) is disposed and built again for that item, in the same place.
/// </summary>
public sealed class For<T, TKey> : Fragment, IRowKeys<TKey>
    where TKey : notnull
{
    private KeyedList? keyed;

    private readonly Func<IReadOnlyList<T>> items;
    private readonly Func<T, TKey> key;
    private readonly Func<T, Node> render;
    private readonly RowBindings<T>? rows;
    // The items are their own keys (no @key), so an item whose key stays is equal to the one it replaces.
    private readonly bool keyIsItem;
    private bool animateExit;

    public For(Func<IReadOnlyList<T>> items, Func<T, TKey> key, Func<T, Node> render)
    {
        this.items = items;
        this.key = key;
        this.render = render;
    }

    internal For(Func<IReadOnlyList<T>> items, Func<T, TKey> key, Func<T, Node> render, bool keyIsItem)
        : this(items, key, render)
    {
        this.keyIsItem = keyIsItem;
    }

    // Rows of a template: after each new list, a row whose key got a different item shows that item.
    internal For(Func<IReadOnlyList<T>> items, Func<T, TKey> key, RowBindings<T> rows, bool keyIsItem)
        : this(items, key, rows.Render, keyIsItem)
    {
        this.rows = rows;
        rows.Keys = this;
    }

    TKey IRowKeys<TKey>.KeyOf(TemplateRow row) => key((T)row.Item!);

    void IRowKeys<TKey>.Changed(TKey k) => keyed?.Invalidate(k);

    bool IRowKeys<TKey>.IsAlive => keyed is { IsDisposed: false };

    /// <summary>
    /// Lets removed items play an exit animation before they leave the DOM, as <see cref="Show.AnimateExit"/> does
    /// for a branch: an item that goes away gets <c>data-state="closed"</c> and stays until its animations finish;
    /// new items get <c>data-state="open"</c>. An item rendered again because its key got a different item is
    /// replaced without animation.
    /// </summary>
    public For<T, TKey> AnimateExit()
    {
        animateExit = true;
        return this;
    }

    protected override void Apply(Element parent)
    {
        var keyed = this.keyed = new KeyedList(parent, Scope.Current) { AnimateExit = animateExit };

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

                Func<int, Node> renderAt = i => render(list[i]);
                if (keyIsItem)
                {
                    keyed.Reconcile(keys, count, renderAt, null);
                }
                else if (rows is not null)
                {
                    // A template row keeps its item itself, and may take a new one in place.
                    keyed.Reconcile(keys, count, renderAt, null);
                    rows.Refresh(keyed, list, renderAt);
                }
                else
                {
                    // A key that stays but has a different item is rendered again for it, in the same place: the
                    // content was built once, for the old item.
                    Func<int, object?> itemAt = i => list[i];
                    keyed.Reconcile(keys, count, renderAt, itemAt);
                    for (var i = 0; i < count; i++)
                    {
                        if (!KeyedList.SameItem((T)keyed.ItemAt(i)!, list[i]))
                        {
                            keyed.Replace(i, renderAt, itemAt);
                        }
                    }
                }
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
    // The items' nodes (and items, when the list keeps them), in the same order as their keys.
    private readonly List<Entry> rendered = new();
    // What the middle of a diff needs, kept from one reconcile to the next and emptied after each.
    private Dictionary<object, int>? oldIndex;
    private HashSet<object>? newKeys;
    private List<int>? tails;
    private int[] sequence = Array.Empty<int>();
    private int[] predecessor = Array.Empty<int>();
    private bool[] stable = Array.Empty<bool>();
    private Entry[] middle = Array.Empty<Entry>();

    public KeyedList(Element parent, Scope? owner)
    {
        this.owner = owner;
        Renderer.AppendChild(parent.Id, anchor.Id);
        // Item scopes are detached from the owner (so removed items don't pile up in it); the list
        // disposes whatever items are left when the owner goes away.
        owner?.Register(this);
    }

    internal int Count => count;

    /// <summary>Removed items animate out (see <see cref="For{T, TKey}.AnimateExit"/>), so they leave one by one.</summary>
    internal bool AnimateExit { get; set; }

    internal object KeyAt(int index) => order[index];

    internal Node NodeAt(int index) => rendered[index].Node;

    /// <summary>The item the content at <paramref name="index"/> was rendered for, when the list keeps items.</summary>
    internal object? ItemAt(int index) => rendered[index].Item;

    /// <summary>The buffer for the next list's keys; pass it to <see cref="Reconcile"/>.</summary>
    internal object[] KeyBuffer(int size)
    {
        if (spare.Length < size)
        {
            spare = new object[Math.Max(size, spare.Length * 2)];
        }

        return spare;
    }

    internal bool IsDisposed { get; private set; }

    // The row with this key, if any, binds again (see IRowKeys).
    internal void Invalidate(object key)
    {
        if (entries.TryGetValue(key, out var entry) && entry.Node is RowNode node)
        {
            node.Row.MarkDirty();
        }
    }

    public void Dispose()
    {
        IsDisposed = true;
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
        rendered.Clear();
    }

    // Most keys of a new list are the very objects of the old one (see For), so try the reference first.
    private static bool Same(object left, object right) => ReferenceEquals(left, right) || left.Equals(right);

    /// <summary>
    /// Whether a key's item is still the one its content was rendered for: the same object, or for a value type
    /// an equal value. When it is not, the content has to show the new item (see <see cref="Replace"/>).
    /// </summary>
    internal static bool SameItem<T>(T rendered, T item) =>
        typeof(T).IsValueType ? EqualityComparer<T>.Default.Equals(rendered, item) : ReferenceEquals(rendered, item);

    /// <summary>
    /// Renders the item at <paramref name="index"/> again, in place: what was rendered for it is removed and
    /// disposed, and the new node goes where the old one was. The key, and every other item, stay as they are.
    /// </summary>
    internal void Replace(int index, Func<int, Node> render, Func<int, object?>? item)
    {
        var k = order[index];
        var old = entries[k];
        old.Node.Remove();
        old.Scope.Dispose();
        entries.Remove(k);

        var entry = Build(index, render, item);
        entries[k] = entry;
        rendered[index] = entry;
        Renderer.InsertBefore(index + 1 < count ? rendered[index + 1].Node.Id : anchor.Id, entry.Node.Id);
    }

    // Renders an item inside a scope of its own, which the list disposes when the item goes.
    private Entry Build(int index, Func<int, Node> render, Func<int, object?>? item)
    {
        var scope = Scope.Detached(owner);
        var previousOwner = Tracker.CurrentOwner;
        Tracker.CurrentOwner = scope;
        try
        {
            var node = render(index);
            Show.Enter(node, AnimateExit);
            return new Entry(node, scope, item?.Invoke(index));
        }
        finally
        {
            Tracker.CurrentOwner = previousOwner;
        }
    }

    // The new keys become the order, and the old order becomes the buffer for the next list.
    private void Adopt(object[] newOrder, int newCount)
    {
        Array.Clear(order, 0, count);
        spare = order;
        order = newOrder;
        count = newCount;
    }

    public void Reconcile(object[] newOrder, int newCount, Func<int, Node> render, Func<int, object?>? item)
    {
        var oldCount = count;

        // An empty list: detach every row with one op and dispose them; nothing to diff.
        if (newCount == 0)
        {
            if (oldCount > 1 && !AnimateExit)
            {
                Renderer.RemoveRange(rendered[0].Node.Id, anchor.Id);
            }
            else
            {
                for (var i = 0; i < oldCount; i++)
                {
                    Show.Leave(rendered[i].Node, AnimateExit);
                }
            }

            for (var i = 0; i < oldCount; i++)
            {
                entries[order[i]].Scope.Dispose();
            }

            entries.Clear();
            rendered.Clear();
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
                var first = rendered[start];
                var last = rendered[oldEnd - 1];
                Renderer.InsertBefore(length > 2 ? rendered[start + 1].Node.Id : first.Node.Id, last.Node.Id);
                if (length > 2)
                {
                    Renderer.InsertBefore(oldEnd < oldCount ? rendered[oldEnd].Node.Id : anchor.Id, first.Node.Id);
                }

                rendered[start] = last;
                rendered[oldEnd - 1] = first;
                Adopt(newOrder, newCount);
                return;
            }
        }

        var size = newEnd - start;
        Dictionary<object, int>? index = null;
        HashSet<object>? fresh = null;
        try
        {
            // Validate before touching anything: no key twice among the new ones.
            var old = oldEnd - start;
            fresh = size > 1 || old > 0 ? newKeys ??= new HashSet<object>() : null;
            if (fresh is not null)
            {
                for (var i = start; i < newEnd; i++)
                {
                    if (!fresh.Add(newOrder[i]))
                    {
                        throw DuplicateKey(newOrder[i]);
                    }
                }
            }

            // Does any key of the old middle survive? When none does (a replaced list), nothing asks where an
            // old key was, so the table of old positions is not built.
            var survives = false;
            for (var i = start; !survives && i < oldEnd; i++)
            {
                survives = fresh!.Contains(order[i]);
            }

            if (survives)
            {
                index = oldIndex ??= new Dictionary<object, int>();
                for (var i = start; i < oldEnd; i++)
                {
                    index[order[i]] = i;
                }
            }

            // A middle key that exists but is not in the old middle is a prefix/suffix key, i.e. a duplicate.
            for (var i = start; i < newEnd; i++)
            {
                var k = newOrder[i];
                if (entries.ContainsKey(k) && (index is null || !index.ContainsKey(k)))
                {
                    throw DuplicateKey(k);
                }
            }

            // Remove entries of the old middle that disappeared. Each item is one node and they sit in order
            // before the next item (or the anchor), so when none of the old middle survives (replace)
            // one range op detaches them all.
            if (old > 0)
            {
                var bulk = !survives && old > 1 && !AnimateExit;
                if (bulk)
                {
                    var endId = oldEnd < oldCount ? rendered[oldEnd].Node.Id : anchor.Id;
                    Renderer.RemoveRange(rendered[start].Node.Id, endId);
                }

                for (var i = start; i < oldEnd; i++)
                {
                    var k = order[i];
                    if (!survives || !fresh!.Contains(k))
                    {
                        entries.Remove(k, out var entry);
                        if (!bulk)
                        {
                            Show.Leave(entry.Node, AnimateExit);
                        }

                        entry.Scope.Dispose();
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
                middle = new Entry[size];
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
            var referenceId = oldEnd < oldCount ? rendered[oldEnd].Node.Id : anchor.Id;
            for (var i = newEnd - 1; i >= start; i--)
            {
                var k = newOrder[i];
                int nodeId;

                if (sequence[i - start] >= 0)
                {
                    middle[i - start] = entries[k];
                    nodeId = middle[i - start].Node.Id;
                    if (!stable[i - start])
                    {
                        Renderer.InsertBefore(referenceId, nodeId);
                    }
                }
                else
                {
                    var entry = Build(i, render, item);
                    entries[k] = entry;
                    middle[i - start] = entry;
                    nodeId = entry.Node.Id;
                    Renderer.InsertBefore(referenceId, nodeId);
                }

                referenceId = nodeId;
            }

            if (size == oldEnd - start)
            {
                for (var i = 0; i < size; i++)
                {
                    rendered[start + i] = middle[i];
                }
            }
            else
            {
                rendered.RemoveRange(start, oldEnd - start);
                rendered.InsertRange(start, new ArraySegment<Entry>(middle, 0, size));
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
        public Entry(Node node, Scope scope, object? item)
        {
            Node = node;
            Scope = scope;
            Item = item;
        }

        public Node Node { get; }
        public Scope Scope { get; }
        // The item the node was rendered for; only kept by lists that render it again when it changes.
        public object? Item { get; }
    }
}