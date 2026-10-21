using System;

namespace NetWasm.UI.Reactive;

/// <summary>
/// A typed key for passing a value down the component tree without threading it through every
/// constructor. The context object itself is the key (reference identity), so no reflection is needed.
/// <code>
/// public static readonly Context&lt;AppState&gt; State = new("AppState");
/// // ancestor, while building:   Provide(State, appState);
/// // descendant, while building: var s = Use(State);
/// </code>
/// </summary>
public sealed class Context<T>
{
    private readonly bool hasDefault;
    private readonly T defaultValue;

    /// <summary>A context without a default; <see cref="Use"/> throws when no ancestor provided a value.</summary>
    public Context(string name)
    {
        Name = name;
        defaultValue = default!;
    }

    /// <summary>A context that falls back to <paramref name="defaultValue"/> when nothing was provided.</summary>
    public Context(string name, T defaultValue)
    {
        Name = name;
        this.defaultValue = defaultValue;
        hasDefault = true;
    }

    public string Name { get; }

    /// <summary>Provides <paramref name="value"/> to everything built afterwards in the current scope and its descendants.</summary>
    public void Provide(T value)
    {
        var scope = Scope.Current ?? throw new InvalidOperationException(
            $"Provide({Name}) must be called while building a component (there is no current scope).");
        scope.SetContext(this, value);
    }

    /// <summary>
    /// Returns the nearest provided value. Call it while building (in <c>Build()</c> or a render
    /// function), not inside a reactive lambda, where there is no current scope.
    /// </summary>
    public T Use()
    {
        if (Scope.Current is { } scope && scope.TryGetContext(this, out var value))
        {
            return (T)value!;
        }

        if (hasDefault)
        {
            return defaultValue;
        }

        throw new InvalidOperationException(
            $"No value provided for context '{Name}'. Call Provide in an ancestor component, and call Use while building (not inside a reactive lambda).");
    }

    public override string ToString() => $"Context<{Name}>";
}
