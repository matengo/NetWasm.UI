using System;
using System.Collections.Generic;
using NetWasm.UI;
using NetWasm.UI.Reactive;
using static NetWasm.UI.Html;

namespace Primitives.Kit;

/// <summary>
/// The root of a compound tabs component (the WAI-ARIA tabs pattern):
/// <c>&lt;Tabs&gt;&lt;TabList&gt;&lt;Tab Value="a"&gt;…&lt;/Tab&gt;&lt;/TabList&gt;&lt;TabPanel Value="a"&gt;…&lt;/TabPanel&gt;&lt;/Tabs&gt;</c>.
/// The parts find each other through a context, so the caller decides the markup between them.
/// </summary>
public sealed class Tabs : Component
{
    /// <summary>The selected tab's value, owned by the caller. Leave it null to let the component keep its own.</summary>
    public Signal<string>? Value { get; set; }

    /// <summary>The initially selected value when <see cref="Value"/> is not set. Empty selects the first tab.</summary>
    public string DefaultValue { get; set; } = "";

    /// <summary>Called with the new value when the user selects a tab.</summary>
    public Action<string>? OnValueChange { get; set; }

    /// <summary>Arrow up and down instead of left and right.</summary>
    public bool Vertical { get; set; }

    public Slot? ChildContent { get; set; }

    protected override Node Build()
    {
        var state = new TabsState(UseId("tabs"), Value ?? new Signal<string>(DefaultValue), Vertical, OnValueChange);
        Provide(TabsState.Context, state);
        return Div(
            Attr("data-orientation", Vertical ? "vertical" : "horizontal"),
            Splat(Attributes),
            ChildContent is null ? Spread() : ChildContent);
    }
}

/// <summary>The row of <see cref="Tab"/>s.</summary>
public sealed class TabList : Component
{
    public Slot? ChildContent { get; set; }

    protected override Node Build()
    {
        var state = Use(TabsState.Context);
        return Div(
            Attr("role", "tablist"),
            Attr("aria-orientation", state.Vertical ? "vertical" : "horizontal"),
            Splat(Attributes),
            ChildContent is null ? Spread() : ChildContent);
    }
}

/// <summary>
/// A tab button. Only the selected tab is in the tab order (roving tabindex); the arrow keys, Home and End
/// move the selection and the focus between the tabs in the order they were built.
/// </summary>
public sealed class Tab : Component
{
    /// <summary>Identifies the tab and its <see cref="TabPanel"/>. Used in element ids, so keep it id-safe.</summary>
    public string Value { get; set; } = "";

    public Slot? ChildContent { get; set; }

    protected override Node Build()
    {
        var state = Use(TabsState.Context);
        var value = Value;
        bool Selected() => state.Selected.Value == value;

        Element button = null!;
        button = Button(
            Type("button"),
            Id(state.TabId(value)),
            Attr("role", "tab"),
            Attr("aria-controls", state.PanelId(value)),
            Attr("aria-selected", () => Selected() ? "true" : "false"),
            Attr("tabindex", () => Selected() ? "0" : "-1"),
            Attr("data-state", () => Selected() ? "active" : "inactive"),
            Splat(Attributes),
            OnClick(() => state.Select(value)),
            OnKeyDown(e =>
            {
                var to = RovingFocus.Move(e.Key, state.IndexOf(value), state.Count, state.Vertical);
                if (to >= 0)
                {
                    state.Select(state.ValueAt(to));
                    state.ElementAt(to).Focus();
                }
            }),
            ChildContent is null ? Spread() : ChildContent);

        state.Register(value, button);
        OnCleanup(() => state.Unregister(value));
        return button;
    }
}

/// <summary>The content for the <see cref="Tab"/> with the same <see cref="Value"/>; hidden while another tab is selected.</summary>
public sealed class TabPanel : Component
{
    public string Value { get; set; } = "";

    public Slot? ChildContent { get; set; }

    protected override Node Build()
    {
        var state = Use(TabsState.Context);
        var value = Value;
        bool Selected() => state.Selected.Value == value;
        return Div(
            Id(state.PanelId(value)),
            Attr("role", "tabpanel"),
            Attr("aria-labelledby", state.TabId(value)),
            Attr("tabindex", "0"),
            Attr("data-state", () => Selected() ? "active" : "inactive"),
            Attr("hidden", () => Selected() ? null : ""),
            Splat(Attributes),
            ChildContent is null ? Spread() : ChildContent);
    }
}

/// <summary>What the parts of <see cref="Tabs"/> share. Internal: callers talk to the components only.</summary>
internal sealed class TabsState
{
    public static readonly Context<TabsState> Context = new("Primitives.Kit.Tabs");

    private readonly string id;
    private readonly Action<string>? onChange;
    private readonly List<(string Value, Element Element)> tabs = new();

    public TabsState(string id, Signal<string> selected, bool vertical, Action<string>? onChange)
    {
        this.id = id;
        this.onChange = onChange;
        Selected = selected;
        Vertical = vertical;
    }

    public Signal<string> Selected { get; }

    public bool Vertical { get; }

    public int Count => tabs.Count;

    public string TabId(string value) => id + "-tab-" + value;

    public string PanelId(string value) => id + "-panel-" + value;

    public void Register(string value, Element element)
    {
        tabs.Add((value, element));
        if (Selected.Value.Length == 0)
        {
            Selected.Value = value;   // no default: the first tab starts selected
        }
    }

    public void Unregister(string value) => tabs.RemoveAt(IndexOf(value));

    public int IndexOf(string value)
    {
        for (var i = 0; i < tabs.Count; i++)
        {
            if (tabs[i].Value == value)
            {
                return i;
            }
        }

        return -1;
    }

    public string ValueAt(int index) => tabs[index].Value;

    public Element ElementAt(int index) => tabs[index].Element;

    public void Select(string value)
    {
        if (Selected.Value != value)
        {
            Selected.Value = value;
            onChange?.Invoke(value);
        }
    }
}
