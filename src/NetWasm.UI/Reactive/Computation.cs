using System;

namespace NetWasm.UI.Reactive;

/// <summary>
/// A list of links (item + the index of the back-link on the other side) that keeps the first link inline,
/// so the common case – one source, one observer – allocates nothing.
/// </summary>
internal struct Links<T>
    where T : class
{
    private T? first;
    private int firstSlot;
    private T[]? rest;
    private int[]? restSlots;

    public int Count;

    public T Item(int i) => i == 0 ? first! : rest![i - 1];

    public int Slot(int i) => i == 0 ? firstSlot : restSlots![i - 1];

    public void SetSlot(int i, int slot)
    {
        if (i == 0)
        {
            firstSlot = slot;
        }
        else
        {
            restSlots![i - 1] = slot;
        }
    }

    public void Set(int i, T item, int slot)
    {
        if (i == 0)
        {
            first = item;
            firstSlot = slot;
        }
        else
        {
            rest![i - 1] = item;
            restSlots![i - 1] = slot;
        }
    }

    /// <summary>Appends a link and returns its index.</summary>
    public int Add(T item, int slot)
    {
        var i = Count;
        if (i > 0)
        {
            if (rest is null)
            {
                rest = new T[4];
                restSlots = new int[4];
            }
            else if (i > rest.Length)
            {
                Array.Resize(ref rest, rest.Length * 2);
                Array.Resize(ref restSlots, rest.Length);
            }
        }

        Set(i, item, slot);
        Count = i + 1;
        return i;
    }

    public void RemoveLast()
    {
        var i = --Count;
        if (i == 0)
        {
            first = null;
        }
        else
        {
            rest![i - 1] = null!;
        }
    }

    public T[] ToArray()
    {
        var result = new T[Count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = Item(i);
        }

        return result;
    }
}

/// <summary>
/// The observer list of something that can be read inside a tracking computation (a signal, a
/// computed, a selector key). Links are bidirectional and remember their index on both sides, so
/// subscribing and unsubscribing are O(1) no matter how many observers a source has.
/// </summary>
internal class SourceNode
{
    // Slot(i) is the index of this source in Item(i)'s source list.
    internal Links<Computation> Observers;

    /// <summary>Registers the currently running computation, if any, as an observer.</summary>
    public void Track() => Tracker.Current?.Link(this);

    /// <summary>Marks every observer dirty.</summary>
    public void Notify()
    {
        var count = Observers.Count;
        if (count == 0)
        {
            return;
        }

        if (count == 1)
        {
            Observers.Item(0).MarkDirty();
            return;
        }

        // Copy because MarkDirty may re-track (and so reorder) the observer list.
        var snapshot = Observers.ToArray();
        for (var i = 0; i < snapshot.Length; i++)
        {
            snapshot[i].MarkDirty();
        }
    }

    /// <summary>Called when the last observer unsubscribed.</summary>
    internal virtual void Unobserved()
    {
    }
}

/// <summary>A unit of reactive work that re-runs when one of its sources changes.</summary>
public abstract class Computation : IDisposable
{
    // Slot(i) is the index of this computation in Item(i).Observers.
    private Links<SourceNode> sources;
    private bool disposed;

    internal bool IsDisposed => disposed;

    internal void Link(SourceNode source)
    {
        var index = sources.Count;
        if (index > 0 && sources.Item(index - 1) == source)
        {
            return; // Cheap dedupe of the common "read the same signal twice in a row".
        }

        var slot = source.Observers.Add(this, index);
        sources.Add(source, slot);
    }

    internal void ClearSources()
    {
        for (var i = sources.Count - 1; i >= 0; i--)
        {
            var source = sources.Item(i);
            var slot = sources.Slot(i);
            var last = source.Observers.Count - 1;
            if (slot < last)
            {
                // Swap-remove: move the last observer into the freed slot and fix its back-pointer.
                var moved = source.Observers.Item(last);
                var movedSlot = source.Observers.Slot(last);
                source.Observers.Set(slot, moved, movedSlot);
                moved.sources.SetSlot(movedSlot, slot);
            }

            source.Observers.RemoveLast();
            sources.RemoveLast();
            if (last == 0)
            {
                source.Unobserved();
            }
        }
    }

    /// <summary>Called by a source when its value changed.</summary>
    internal abstract void MarkDirty();

    public virtual void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        ClearSources();
    }
}

/// <summary>Tracks which computation is currently executing so sources can register dependents.</summary>
internal static class Tracker
{
    public static Computation? Current;
    public static Scope? CurrentOwner;

    /// <summary>Runs <paramref name="action"/> without dependency tracking.</summary>
    public static void Untracked(Action action)
    {
        var previous = Current;
        Current = null;
        try
        {
            action();
        }
        finally
        {
            Current = previous;
        }
    }

    public static T Untracked<T>(Func<T> func)
    {
        var previous = Current;
        Current = null;
        try
        {
            return func();
        }
        finally
        {
            Current = previous;
        }
    }
}
