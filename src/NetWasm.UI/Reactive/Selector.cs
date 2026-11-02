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

    public Selector(IReadOnlySignal<TKey> source, IEqualityComparer<TKey>? comparer = null)
    {
        this.source = source;
        this.comparer = comparer ?? EqualityComparer<TKey>.Default;
        keys = new Dictionary<TKey, KeyNode>(this.comparer);
        Tracker.CurrentOwner?.Register(this);
        current = Read();
    }

    /// <summary>True when <paramref name="key"/> equals the source's value. Tracks only this key.</summary>
    public bool Is(TKey key)
    {
        if (Tracker.Current is not null && !IsDisposed)
        {
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
