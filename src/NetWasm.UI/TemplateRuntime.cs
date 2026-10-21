using System;
using System.Collections.Generic;
using System.Globalization;
using NetWasm.UI.Reactive;

namespace NetWasm.UI;

/// <summary>Helpers called by code generated from <c>.nwui</c> templates. Not meant to be called by hand.</summary>
public static class TemplateRuntime
{
    /// <summary>Formats a value for an attribute or text: null stays null, numbers etc. use the invariant culture.</summary>
    public static string? Str<T>(T value) => value switch
    {
        null => null,
        string s => s,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    /// <summary><c>@if / else if / else</c>: <paramref name="selector"/> returns the index of the branch to show (or -1 for none).</summary>
    public static Content Chain(Func<int> selector, params Func<Node>[] branches)
    {
        var selected = new Computed<int>(selector);
        var parts = new Content[branches.Length];
        for (var i = 0; i < branches.Length; i++)
        {
            var index = i;
            parts[i] = Html.When(() => selected.Value == index, branches[i]);
        }

        return Html.Spread(parts);
    }

    public static For<T, TKey> Each<T, TKey>(Func<IReadOnlyList<T>> items, Func<T, TKey> key, Func<T, Node> render)
        where TKey : notnull =>
        new(items, key, render);

    /// <summary><c>@foreach (var x in signal)</c>: the signal's list.</summary>
    public static For<T, TKey> Each<T, TKey>(Func<IReadOnlySignal<IReadOnlyList<T>>> items, Func<T, TKey> key, Func<T, Node> render)
        where TKey : notnull =>
        new(() => items().Value, key, render);

    /// <summary>Without <c>@key</c> the items themselves are the keys.</summary>
    public static For<T, T> Each<T>(Func<IReadOnlyList<T>> items, Func<T, Node> render)
        where T : notnull =>
        new(items, static x => x, render);

    public static For<T, T> Each<T>(Func<IReadOnlySignal<IReadOnlyList<T>>> items, Func<T, Node> render)
        where T : notnull =>
        new(() => items().Value, static x => x, render);
}
