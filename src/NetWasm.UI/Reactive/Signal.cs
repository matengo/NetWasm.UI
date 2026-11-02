using System.Collections.Generic;

namespace NetWasm.UI.Reactive;

/// <summary>A readable reactive value.</summary>
public interface IReadOnlySignal<out T>
{
    T Value { get; }
}

/// <summary>A mutable reactive value. Reads inside an <see cref="Effect"/> or <see cref="Computed{T}"/> register a dependency.</summary>
public sealed class Signal<T> : IReadOnlySignal<T>
{
    private readonly SourceNode observers = new();
    private readonly IEqualityComparer<T> comparer;
    private T value;

    public Signal(T initial, IEqualityComparer<T>? comparer = null)
    {
        value = initial;
        this.comparer = comparer ?? EqualityComparer<T>.Default;
    }

    public T Value
    {
        get
        {
            observers.Track();
            return value;
        }
        set
        {
            if (comparer.Equals(value, this.value))
            {
                return;
            }

            this.value = value;
            Notify();
        }
    }

    /// <summary>Reads the value without registering a dependency.</summary>
    public T Peek() => value;

    public void Update(System.Func<T, T> updater) => Value = updater(value);

    /// <summary>Forces dependents to re-run even if the value compares equal (useful for mutated collections).</summary>
    public void Touch() => Notify();

    private void Notify()
    {
        Scheduler.BeginBatch();
        try
        {
            observers.Notify();
        }
        finally
        {
            Scheduler.EndBatch();
        }
    }

    /// <summary>The current value, formatted with the invariant culture. Tracked, so <c>@count</c> in a template stays reactive.</summary>
    public override string ToString() => TemplateRuntime.Str(Value) ?? string.Empty;
}
