using System;
using System.Collections.Generic;

namespace NetWasm.UI.Reactive;

/// <summary>
/// Owns computations and other disposables created while it is the current owner.
/// Disposing a scope disposes everything created inside it, which is how conditional
/// and list rendering tear down their subtrees without any runtime reflection.
/// Scopes form a tree (via <see cref="Parent"/>) that <see cref="Context{T}"/> lookups walk.
/// </summary>
public sealed class Scope : IDisposable
{
    private IDisposable[]? owned;
    private int ownedCount;
    private Dictionary<object, object?>? contexts;
    private bool disposed;

    /// <summary>Creates a scope owned by the current owner, if any.</summary>
    public Scope()
        : this(Tracker.CurrentOwner)
    {
    }

    /// <summary>Creates a scope owned by an explicit parent (or a root scope when <paramref name="parent"/> is null).</summary>
    public Scope(Scope? parent)
        : this(parent, register: true)
    {
    }

    private Scope(Scope? parent, bool register)
    {
        Parent = parent;
        if (register)
        {
            parent?.Register(this);
        }
    }

    /// <summary>
    /// A scope that sees <paramref name="parent"/>'s context but is not disposed by it. The caller owns
    /// its lifetime; used for list items, which come and go long before their parent is disposed.
    /// </summary>
    internal static Scope Detached(Scope? parent) => new(parent, register: false);

    internal static Scope? Current => Tracker.CurrentOwner;

    public Scope? Parent { get; }

    public bool IsDisposed => disposed;

    /// <summary>Receives errors raised inside this scope or its descendants (set by <see cref="ErrorBoundary"/>).</summary>
    internal Action<Exception>? ErrorHandler { get; set; }

    public void Register(IDisposable disposable)
    {
        if (owned is null)
        {
            owned = new IDisposable[8];
        }
        else if (ownedCount == owned.Length)
        {
            Array.Resize(ref owned, ownedCount * 2);
        }

        owned[ownedCount++] = disposable;
    }

    internal void SetContext(object key, object? value) => (contexts ??= new Dictionary<object, object?>())[key] = value;

    /// <summary>Looks <paramref name="key"/> up in this scope and its ancestors.</summary>
    internal bool TryGetContext(object key, out object? value)
    {
        for (var s = this; s is not null; s = s.Parent)
        {
            if (s.contexts is not null && s.contexts.TryGetValue(key, out value))
            {
                return true;
            }
        }

        value = null;
        return false;
    }

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
        var list = owned;
        if (list is not null)
        {
            for (var i = ownedCount - 1; i >= 0; i--)
            {
                list[i].Dispose();
            }
        }

        owned = null;
        ownedCount = 0;
        contexts = null;
        ErrorHandler = null;
    }
}
