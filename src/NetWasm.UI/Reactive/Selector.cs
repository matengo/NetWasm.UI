using System.Collections.Generic;

namespace NetWasm.UI.Reactive;

/// <summary>
/// Like SolidJS's <c>createSelector</c>: answers "is <c>key</c> the current value of the source?" while
/// tracking only that key. When the source changes, just the readers of the previous key and of the new
/// key re-run, instead of every reader of the source. Use it for "selected row" in a list of thousands.
/// </summary>
/// <example><code>
/// var isSelected = new Selector&lt;int&gt;(selectedId);
/// Tr(Class(() =&gt; isSelected.Is(row.Id) ? "danger" : null), ...)
/// </code></example>
public sealed class Selector<TKey> : Computation
    where TKey : notnull
{
    private readonly IReadOnlySignal<TKey> source;
    private readonly IEqualityComparer<TKey> comparer;
    private readonly Dictionary<TKey, KeyNode> keys;
    private TKey current;
    // Lists of template rows whose rows ask about their own key (see IRowKeys).
    private List<IRowKeys<TKey>>? lists;
    private readonly bool defaultComparer;

    public Selector(IReadOnlySignal<TKey> source, IEqualityComparer<TKey>? comparer = null)
    {
        this.source = source;
        this.comparer = comparer ?? EqualityComparer<TKey>.Default;
        keys = new Dictionary<TKey, KeyNode>(this.comparer);
        defaultComparer = ReferenceEquals(this.comparer, EqualityComparer<TKey>.Default);
        Tracker.CurrentOwner?.Register(this);
        current = Read();
    }

    /// <summary>True when <paramref name="key"/> equals the source's value. Tracks only this key.</summary>
    public bool Is(TKey key)
    {
        var running = Tracker.Current;
        if (running is not null && !IsDisposed)
        {
            // The list finds a row by its key with the key's own Equals, so only a selector that compares the same
            // way can leave the row to its list.
            if (defaultComparer && running is TemplateRow row && row.Keys is IRowKeys<TKey> rows &&
                comparer.Equals(rows.KeyOf(row), key))
            {
                Watch(rows);
                return comparer.Equals(key, current);
            }

            if (!keys.TryGetValue(key, out var node))
            {
                node = new KeyNode(this, key);
                keys[key] = node;
            }

            node.Track();
        }

        return comparer.Equals(key, current);
    }

    internal override void MarkDirty()
    {
        if (IsDisposed)
        {
            return;
        }

        var previous = current;
        current = Read();
        if (comparer.Equals(previous, current))
        {
            return;
        }

        Scheduler.BeginBatch();
        try
        {
            Notify(previous);
            Notify(current);
            NotifyLists(previous);
            NotifyLists(current);
        }
        finally
        {
            Scheduler.EndBatch();
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        keys.Clear();
        lists = null;
    }

    private void Watch(IRowKeys<TKey> rows)
    {
        var all = lists ??= new List<IRowKeys<TKey>>();
        for (var i = 0; i < all.Count; i++)
        {
            if (ReferenceEquals(all[i], rows))
            {
                return;
            }
        }

        all.Add(rows);
    }

    private void NotifyLists(TKey key)
    {
        var all = lists;
        if (key is null || all is null)
        {
            return;
        }

        for (var i = all.Count - 1; i >= 0; i--)
        {
            if (all[i].IsAlive)
            {
                all[i].Changed(key);
            }
            else
            {
                all.RemoveAt(i);
            }
        }
    }

    private void Notify(TKey key)
    {
        if (key is not null && keys.TryGetValue(key, out var node))
        {
            node.Notify();
        }
    }

    private TKey Read()
    {
        ClearSources();
        var previous = Tracker.Current;
        Tracker.Current = this;
        try
        {
            return source.Value;
        }
        finally
        {
            Tracker.Current = previous;
        }
    }

    private sealed class KeyNode : SourceNode
    {
        private readonly Selector<TKey> owner;
        private readonly TKey key;

        public KeyNode(Selector<TKey> owner, TKey key)
        {
            this.owner = owner;
            this.key = key;
        }

        // Forget keys nobody reads any more (e.g. removed rows), so the map doesn't grow forever.
        internal override void Unobserved()
        {
            if (owner.keys.TryGetValue(key, out var node) && node == this)
            {
                owner.keys.Remove(key);
            }
        }
    }
}
