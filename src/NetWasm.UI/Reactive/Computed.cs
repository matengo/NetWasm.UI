using System;
using System.Collections.Generic;

namespace NetWasm.UI.Reactive;

/// <summary>A lazily evaluated derived value. Recomputes on the next read after a dependency changed.</summary>
public sealed class Computed<T> : Computation, IReadOnlySignal<T>
{
    private readonly Func<T> compute;
    private readonly SourceNode observers = new();
    private readonly IEqualityComparer<T> comparer;
    private T value = default!;
    private bool stale = true;

    public Computed(Func<T> compute, IEqualityComparer<T>? comparer = null)
    {
        this.compute = compute;
        this.comparer = comparer ?? EqualityComparer<T>.Default;
        Tracker.CurrentOwner?.Register(this);
    }

    public T Value
    {
        get
        {
            observers.Track();
            if (stale)
            {
                Recompute();
            }

            return value;
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

        stale = false;
    }

    internal override void MarkDirty()
    {
        if (stale || IsDisposed)
        {
            return;
        }

        var previousValue = value;
        // Recompute eagerly so that equal results do not propagate.
        Recompute();
        if (comparer.Equals(previousValue, value))
        {
            return;
        }

        observers.Notify();
    }

    /// <summary>The current value, formatted with the invariant culture. Tracked, so <c>@doubled</c> in a template stays reactive.</summary>
    public override string ToString() => TemplateRuntime.Str(Value) ?? string.Empty;
}
