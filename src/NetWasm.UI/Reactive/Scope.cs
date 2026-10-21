using System;
using System.Collections.Generic;

namespace NetWasm.UI.Reactive;

/// <summary>
/// Owns computations and other disposables created while it is the current owner.
/// Disposing a scope disposes everything created inside it, which is how conditional
/// and list rendering tear down their subtrees without any runtime reflection.
/// </summary>
public sealed class Scope : IDisposable
{
    private readonly List<IDisposable> owned = new();
    private bool disposed;

    /// <summary>Creates a scope owned by the current owner, if any.</summary>
    public Scope()
        : this(Tracker.CurrentOwner)
    {
    }

    /// <summary>Creates a scope owned by an explicit parent (or a root scope when <paramref name="parent"/> is null).</summary>
    public Scope(Scope? parent)
    {
        parent?.Register(this);
    }

    internal static Scope? Current => Tracker.CurrentOwner;

    public void Register(IDisposable disposable) => owned.Add(disposable);

    /// <summary>Runs <paramref name="action"/> with this scope as the current owner.</summary>
    public void Run(Action action)
    {
        var previous = Tracker.CurrentOwner;
        Tracker.CurrentOwner = this;
        try
        {
            action();
        }
        finally
        {
            Tracker.CurrentOwner = previous;
        }
    }

    public T Run<T>(Func<T> func)
    {
        var previous = Tracker.CurrentOwner;
        Tracker.CurrentOwner = this;
        try
        {
            return func();
        }
        finally
        {
            Tracker.CurrentOwner = previous;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        // Dispose in reverse creation order so children go before parents.
        for (var i = owned.Count - 1; i >= 0; i--)
        {
            owned[i].Dispose();
        }

        owned.Clear();
    }
}
