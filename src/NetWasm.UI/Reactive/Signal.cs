using System.Collections.Generic;

namespace NetWasm.UI.Reactive;

/// <summary>A readable reactive value.</summary>
public interface IReadOnlySignal<out T>
{
    T Value { get; }
}

/// <summary>A mutable reactive value. Reads inside an <see cref="Effect"/> or <see cref="Computed{T}"/> register a dependency.</summary>
public sealed class Signal<T> : IReadOnlySignal<T>, ISource
{
    private readonly List<Computation> dependents = new();
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
            var current = Tracker.Current;
            if (current is not null)
            {
                AddDependent(current);
                current.AddSource(this);
            }

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
        Scheduler.Batch(() =>
        {
            // Copy because MarkDirty may mutate the dependents list.
            var snapshot = dependents.ToArray();
            for (var i = 0; i < snapshot.Length; i++)
            {
                snapshot[i].MarkDirty();
            }
        });
    }

    void ISource.AddDependent(Computation dependent) => AddDependent(dependent);

    private void AddDependent(Computation dependent)
    {
        if (!dependents.Contains(dependent))
        {
            dependents.Add(dependent);
        }
    }

    void ISource.RemoveDependent(Computation dependent) => dependents.Remove(dependent);

    /// <summary>The current value, formatted with the invariant culture. Tracked, so <c>@count</c> in a template stays reactive.</summary>
    public override string ToString() => TemplateRuntime.Str(Value) ?? string.Empty;
}
