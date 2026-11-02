using System;
using System.Collections.Generic;

namespace NetWasm.UI.Reactive;

/// <summary>
/// The observer list of something that can be read inside a tracking computation (a signal, a
/// computed, a selector key). Links are bidirectional and remember their index on both sides, so
/// subscribing and unsubscribing are O(1) no matter how many observers a source has.
/// </summary>
internal class SourceNode
{
    internal readonly List<Computation> Observers = new();

    // ObserverSlots[i] is the index of this source in Observers[i]'s source list.
    internal readonly List<int> ObserverSlots = new();

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
    private List<SourceNode>? sources;

    // sourceSlots[i] is the index of this computation in sources[i].Observers.
    private List<int>? sourceSlots;
    private bool disposed;

    internal bool IsDisposed => disposed;

    internal void Link(SourceNode source)
    {
        sources ??= new List<SourceNode>();
        sourceSlots ??= new List<int>();
        var index = sources.Count;
        if (index > 0 && sources[index - 1] == source)
        {
            return; // Cheap dedupe of the common "read the same signal twice in a row".
        }

        sources.Add(source);
        sourceSlots.Add(source.Observers.Count);
        source.Observers.Add(this);
        source.ObserverSlots.Add(index);
    }

    internal void ClearSources()
    {
        if (sources is null || sourceSlots is null)
        {
            return;
        }

        for (var i = sources.Count - 1; i >= 0; i--)
        {
            var source = sources[i];
            var slot = sourceSlots[i];
            var observers = source.Observers;
            var last = observers.Count - 1;
            if (slot < last)
            {
                // Swap-remove: move the last observer into the freed slot and fix its back-pointer.
                var moved = observers[last];
                var movedSlot = source.ObserverSlots[last];
                observers[slot] = moved;
                source.ObserverSlots[slot] = movedSlot;
                moved.sourceSlots![movedSlot] = slot;
            }

            observers.RemoveAt(last);
            source.ObserverSlots.RemoveAt(last);
            if (last == 0)
            {
                source.Unobserved();
            }
        }

        sources.Clear();
        sourceSlots.Clear();
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
