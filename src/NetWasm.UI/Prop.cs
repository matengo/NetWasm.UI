using System;
using NetWasm.UI.Reactive;

namespace NetWasm.UI;

/// <summary>
/// A component prop that is either a fixed value or a live getter supplied by the parent. Reading
/// <see cref="Value"/> inside a reactive context (<c>@Count</c>, <c>Text(() => …)</c>, an <c>@if</c>)
/// tracks whatever the parent's expression reads, so the child updates when the parent's state changes.
/// Templates pass <c>Count="@expr"</c> as a getter automatically; from C#, pass a value, a signal or
/// <c>new(() => …)</c>.
/// </summary>
public readonly struct Prop<T>
{
    private readonly Func<T>? getter;
    private readonly T value;

    public Prop(T value)
    {
        this.value = value;
        getter = null;
    }

    public Prop(Func<T> getter)
    {
        value = default!;
        this.getter = getter;
    }

    /// <summary>A getter returning a signal (<c>Count="@count"</c>) reads the signal's value.</summary>
    public Prop(Func<IReadOnlySignal<T>> signal)
    {
        value = default!;
        getter = () => signal().Value;
    }

    /// <summary>The current value; tracked when read inside a reactive context.</summary>
    public T Value => getter is null ? value : getter();

    /// <summary>True when the parent passed a getter rather than a fixed value.</summary>
    public bool IsReactive => getter is not null;

    public static implicit operator Prop<T>(T value) => new(value);

    public static implicit operator Prop<T>(Signal<T> signal) => new(() => signal.Value);

    public static implicit operator Prop<T>(Computed<T> computed) => new(() => computed.Value);

    public static implicit operator T(Prop<T> prop) => prop.Value;

    public override string ToString() => TemplateRuntime.Str(Value) ?? string.Empty;
}
