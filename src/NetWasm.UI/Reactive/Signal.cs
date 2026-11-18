using System.Collections.Generic;

namespace NetWasm.UI.Reactive;

/// <summary>A readable reactive value.</summary>
public interface IReadOnlySignal<out T>
{
    T Value { get; }
}

/// <summary>A mutable reactive value. Reads inside an <see cref="Effect"/> or <see cref="Computed{T}"/> register a dependency.</summary>
public sealed class Signal<T> : SourceNode, IReadOnlySignal<T>
{
    private readonly IEqualityComparer<T> comparer;
    private T value;

    public Signal(T initial, IEqualityComparer<T>? comparer = null)
    {
        value = initial;
        this.comparer = comparer ?? EqualityComparer<T>.Default;
        DevState.Tracker?.Invoke(this);
    }

    public T Value
    {
        get
        {
            Track();
            return value;
        }
        set
        {
            if (comparer.Equals(value, this.value))
            {
                return;
            }

            this.value = value;
            NotifyInBatch();
        }
    }

    /// <summary>Reads the value without registering a dependency.</summary>
    public T Peek() => value;

    public void Update(System.Func<T, T> updater) => Value = updater(value);

    /// <summary>Forces dependents to re-run even if the value compares equal (useful for mutated collections).</summary>
    public void Touch() => NotifyInBatch();

    private void NotifyInBatch()
    {
        Scheduler.BeginBatch();
        try
        {
            Notify();
        }
        finally
        {
            Scheduler.EndBatch();
        }
    }

    /// <summary>The current value, formatted with the invariant culture. Tracked, so <c>@count</c> in a template stays reactive.</summary>
    public override string ToString() => TemplateRuntime.Str(Value) ?? string.Empty;
}
