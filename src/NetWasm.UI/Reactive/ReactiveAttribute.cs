using System;
using System.Diagnostics;

namespace NetWasm.UI.Reactive;

/// <summary>
/// Backs a <c>partial</c> property with a <see cref="Signal{T}"/>. The source generator implements the property
/// (<c>get =&gt; signal.Value</c>, <c>set =&gt; signal.Value = value</c>) and adds a <c>NameSignal</c> property
/// that returns the signal itself, for APIs that take a signal (<c>Text(CountSignal)</c>, <c>Bind</c>, props).
/// </summary>
/// <example>
/// <code>
/// public partial class CounterStore
/// {
///     [Reactive(1)] public partial int Count { get; set; }
/// }
/// </code>
/// </example>
/// <remarks>The attribute is only read at compile time and is not kept in the compiled app.</remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
[Conditional("NETWASM_UI_KEEP_ATTRIBUTES")]
public sealed class ReactiveAttribute : Attribute
{
    /// <summary>The signal starts at <c>default</c>.</summary>
    public ReactiveAttribute()
    {
    }

    /// <summary>The signal starts at <paramref name="initial"/>, a constant (number, string, bool, char or enum value).</summary>
    public ReactiveAttribute(object? initial)
    {
        Initial = initial;
    }

    /// <summary>The initial value, or <c>null</c>.</summary>
    public object? Initial { get; }
}
