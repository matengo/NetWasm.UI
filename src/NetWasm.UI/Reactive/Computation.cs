using System;
using System.Collections.Generic;

namespace NetWasm.UI.Reactive;

/// <summary>Something that can be read inside a tracking computation.</summary>
internal interface ISource
{
    void AddDependent(Computation dependent);
    void RemoveDependent(Computation dependent);
}

/// <summary>A unit of reactive work that re-runs when one of its sources changes.</summary>
public abstract class Computation : IDisposable
{
    private readonly List<ISource> sources = new();
    private bool disposed;

    internal bool IsDisposed => disposed;

    internal void AddSource(ISource source) => sources.Add(source);

    internal void ClearSources()
    {
        for (var i = 0; i < sources.Count; i++)
        {
            sources[i].RemoveDependent(this);
        }

        sources.Clear();
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
