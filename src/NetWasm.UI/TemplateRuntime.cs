using System;
using System.Collections.Generic;
using System.Globalization;
using NetWasm.UI.Reactive;

namespace NetWasm.UI;

/// <summary>Helpers called by code generated from <c>.nwui</c> templates. Not meant to be called by hand.</summary>
public static class TemplateRuntime
{
    /// <summary>Formats a value for an attribute or text: null stays null, numbers etc. use the invariant culture.</summary>
    /// <remarks>
    /// The <c>typeof(T)</c> checks fold away per instantiation, so a <c>Str&lt;int&gt;</c> only carries integer
    /// formatting. An <c>IFormattable</c> interface call would make every number type's formatting reachable.
    /// </remarks>
    public static string? Str<T>(T value)
    {
        if (value is null) return null;
        if (typeof(T) == typeof(string)) return (string)(object)value;
        if (typeof(T) == typeof(int)) return ((int)(object)value).ToString(CultureInfo.InvariantCulture);
        if (typeof(T) == typeof(long)) return ((long)(object)value).ToString(CultureInfo.InvariantCulture);
        if (typeof(T) == typeof(double)) return ((double)(object)value).ToString(CultureInfo.InvariantCulture);
        if (typeof(T) == typeof(float)) return ((float)(object)value).ToString(CultureInfo.InvariantCulture);
        if (typeof(T) == typeof(decimal)) return ((decimal)(object)value).ToString(CultureInfo.InvariantCulture);
        if (typeof(T) == typeof(bool)) return (bool)(object)value ? "True" : "False";

        return value.ToString();
    }

    /// <summary>
    /// Like <see cref="Str{T}"/> but for attribute values: a <c>bool</c> becomes <c>true</c>/<c>false</c>
    /// (what <c>aria-*</c> and <c>data-*</c> attributes expect) instead of <c>True</c>/<c>False</c>.
    /// </summary>
    public static string? AttrStr<T>(T value)
    {
        if (typeof(T) == typeof(bool)) return (bool)(object)value! ? "true" : "false";
        if (typeof(T) == typeof(bool?)) return value is null ? null : (bool)(object)value ? "true" : "false";
        return Str(value);
    }

    /// <summary><c>@render(expr)</c> with one argument: the content as-is.</summary>
    public static Content Render(Content content) => content;

    /// <summary><c>@render(Slot, arg)</c>: builds a <see cref="Slot{T}"/> for <paramref name="arg"/>.</summary>
    public static Content Render<T>(Slot<T>? slot, T arg) => slot is null ? Html.Spread() : slot.With(arg);

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

    /// <summary>
    /// A keyed list whose rows are clones of <paramref name="template"/>; what the template compiler emits for a
    /// <c>@foreach</c> over an element. Three functions of the row fill it in, so a row needs no closure, delegate,
    /// effect or listener per binding: <paramref name="setup"/> runs once when the row is created, for content
    /// that is applied once; <paramref name="bind"/> writes the row's reactive text and attributes (each with a
    /// slot below <paramref name="slots"/>) and runs again when something it read changes; <paramref name="events"/>
    /// receives the events of the template's listeners (see <see cref="Template.Listen"/>) with the listener's slot.
    /// </summary>
    /// <remarks>
    /// A row shows the item its key has in the current list. When a new list has a different item for a key that
    /// stays (another object, or for a value type a value that is not equal), a row without
    /// <paramref name="setup"/> takes it in place, and a row with <paramref name="setup"/> is disposed and built
    /// again for the new item.
    /// </remarks>
    public static For<T, TKey> Rows<T, TKey>(Func<IReadOnlyList<T>> items, Func<T, TKey> key, Template template, int slots,
        Action<TemplateRow, T>? setup, Action<TemplateRow, T>? bind, Action<TemplateRow, T, int, DomEvent>? events = null)
        where TKey : notnull =>
        Rows(items, key, new RowBindings<T>(template, slots, setup, bind, events));

    /// <inheritdoc cref="Rows{T, TKey}(Func{IReadOnlyList{T}}, Func{T, TKey}, Template, int, Action{TemplateRow, T}?, Action{TemplateRow, T}?, Action{TemplateRow, T, int, DomEvent}?)"/>
    public static For<T, TKey> Rows<T, TKey>(Func<IReadOnlySignal<IReadOnlyList<T>>> items, Func<T, TKey> key, Template template, int slots,
        Action<TemplateRow, T>? setup, Action<TemplateRow, T>? bind, Action<TemplateRow, T, int, DomEvent>? events = null)
        where TKey : notnull
    {
        var signal = items();
        return Rows(() => signal.Value, key, new RowBindings<T>(template, slots, setup, bind, events));
    }

    /// <summary>Without <c>@key</c> the items themselves are the keys.</summary>
    public static For<T, T> Rows<T>(Func<IReadOnlyList<T>> items, Template template, int slots,
        Action<TemplateRow, T>? setup, Action<TemplateRow, T>? bind, Action<TemplateRow, T, int, DomEvent>? events = null)
        where T : notnull =>
        Rows(items, static item => item, new RowBindings<T>(template, slots, setup, bind, events));

    /// <inheritdoc cref="Rows{T}(Func{IReadOnlyList{T}}, Template, int, Action{TemplateRow, T}?, Action{TemplateRow, T}?, Action{TemplateRow, T, int, DomEvent}?)"/>
    public static For<T, T> Rows<T>(Func<IReadOnlySignal<IReadOnlyList<T>>> items, Template template, int slots,
        Action<TemplateRow, T>? setup, Action<TemplateRow, T>? bind, Action<TemplateRow, T, int, DomEvent>? events = null)
        where T : notnull
    {
        var signal = items();
        return Rows(() => signal.Value, static item => item, new RowBindings<T>(template, slots, setup, bind, events));
    }

    private static For<T, TKey> Rows<T, TKey>(Func<IReadOnlyList<T>> items, Func<T, TKey> key, RowBindings<T> rows)
        where TKey : notnull =>
        new(items, key, rows);

    /// <summary>Calls a row's event handler, whichever of the two shapes <c>@on…</c> accepts it has.</summary>
    public static void Invoke(Action handler, DomEvent e) => handler();

    /// <inheritdoc cref="Invoke(Action, DomEvent)"/>
    public static void Invoke(Action<DomEvent> handler, DomEvent e) => handler(e);

    /// <summary>Without <c>@key</c> the items themselves are the keys.</summary>
    public static For<T, T> Each<T>(Func<IReadOnlyList<T>> items, Func<T, Node> render)
        where T : notnull =>
        new(items, static x => x, render);

    public static For<T, T> Each<T>(Func<IReadOnlySignal<IReadOnlyList<T>>> items, Func<T, Node> render)
        where T : notnull =>
        new(() => items().Value, static x => x, render);
}
