using System;
using System.Collections.Generic;

namespace NetWasm.UI.Reactive;

/// <summary>
/// A lazily evaluated derived value. A change upstream only marks it; it recomputes on the next read, or when
/// an observer that is about to re-run asks for it. Observers re-run only if the result differs from the previous one.
/// </summary>
public sealed class Computed<T> : Computation, IReadOnlySignal<T>
{
    private readonly Func<T> compute;
    private readonly Node observers;
    private readonly IEqualityComparer<T> comparer;
    private T value = default!;
    private bool initialized;

    public Computed(Func<T> compute, IEqualityComparer<T>? comparer = null)
    {
        this.compute = compute;
        this.comparer = comparer ?? EqualityComparer<T>.Default;
        observers = new Node(this);
        State = Dirty;
        Tracker.CurrentOwner?.Register(this);
    }

    public T Value
    {
        get
        {
            if (State != Clean)
            {
                Update();
            }

            // Tracked after updating, so the reader is not told about a change it is reading right now.
            observers.Track();
            return value;
        }
    }

    private void Update()
    {
        if (State == Check)
        {
            ResolveCheck();
            if (State == Clean)
            {
                return;
            }
        }

        var previous = value;
        var first = !initialized;
        Recompute();
        initialized = true;
        if (first || !comparer.Equals(previous, value))
        {
            observers.NotifyChanged();
        }
    }

    private void Recompute()
    {
        ClearSources();
        var previous = Tracker.Current;
        Tracker.Current = this;
        try
        {
            value = compute();
        }
        finally
        {
            Tracker.Current = previous;
        }

        State = Clean;
    }

    internal override void MarkDirty()
    {
        if (State == Dirty || IsDisposed)
        {
            return;
        }

        var wasClean = State == Clean;
        State = Dirty;
        if (wasClean)
        {
            observers.NotifyCheck();
        }
    }

    internal override void MarkCheck()
    {
        if (State != Clean || IsDisposed)
        {
            return;
        }

        State = Check;
        observers.NotifyCheck();
    }

    /// <summary>The current value, formatted with the invariant culture. Tracked, so <c>@doubled</c> in a template stays reactive.</summary>
    public override string ToString() => TemplateRuntime.Str(Value) ?? string.Empty;

    private sealed class Node : SourceNode
    {
        private readonly Computed<T> owner;

        public Node(Computed<T> owner) => this.owner = owner;

        internal override void Refresh()
        {
            if (owner.State != Clean)
            {
                owner.Update();
            }
        }
    }
}