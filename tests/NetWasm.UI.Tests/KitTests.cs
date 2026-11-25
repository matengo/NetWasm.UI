using NetWasm.UI.Reactive;
using NetWasm.UI.Primitives;
using Primitives.Theme;
using static NetWasm.UI.Html;

namespace NetWasm.UI.Tests;

/// <summary>
/// The headless components in src/NetWasm.UI.Primitives (NwUi.Primitives) and the design-system wrapper in samples/Primitives.Theme.
/// Both target netstandard2.1, so this net10.0 project uses them unchanged. docs/component-library-tutorial.md
/// quotes these tests.
/// </summary>
public class KitTests
{
    private static Disclosure NewDisclosure() => new()
    {
        Trigger = new Slot(() => Text("More")),
        ChildContent = new Slot(() => P("Details")),
    };

    [Fact]
    public void Disclosure_toggles_its_panel_and_reports_the_change()
    {
        var changes = new List<bool>();
        var disclosure = NewDisclosure();
        disclosure.OnOpenChange = changes.Add;
        disclosure.Attributes = [Class("faq"), Attr("data-testid", "faq")];
        using var r = TestRenderer.Render(disclosure);

        var root = r.Find("[data-testid=faq]");
        var trigger = r.Find("button");
        var panel = r.Find("[role=region]");
        Assert.Equal("faq", root.GetAttribute("class"));
        Assert.Equal("false", trigger.GetAttribute("aria-expanded"));
        Assert.Equal(panel.GetAttribute("id"), trigger.GetAttribute("aria-controls"));
        Assert.True(panel.HasAttribute("hidden"));

        r.Click(trigger);

        Assert.Equal("true", trigger.GetAttribute("aria-expanded"));
        Assert.Equal("open", root.GetAttribute("data-state"));
        Assert.False(panel.HasAttribute("hidden"));
        Assert.Equal([true], changes);
    }

    [Fact]
    public void Controlled_disclosure_follows_the_callers_signal()
    {
        var open = new Signal<bool>(true);
        var disclosure = NewDisclosure();
        disclosure.Open = open;
        disclosure.Disabled = new Prop<bool>(() => !open.Value);
        using var r = TestRenderer.Render(disclosure);
        var trigger = r.Find("button");
        Assert.Equal("true", trigger.GetAttribute("aria-expanded"));

        r.Click(trigger);

        Assert.False(open.Value);
        Assert.True(trigger.HasAttribute("disabled"));

        open.Value = true;

        Assert.Equal("open", r.Find("[role=region]").GetAttribute("data-state"));
        Assert.False(trigger.HasAttribute("disabled"));
    }

    private static Tabs NewTabs(Action<string>? onChange = null) => new()
    {
        OnValueChange = onChange,
        ChildContent = new Slot(() => new Content[]
        {
            new TabList { ChildContent = new Slot(() => new Content[]
            {
                new Tab { Value = "a", ChildContent = new Slot(() => Text("A")) },
                new Tab { Value = "b", ChildContent = new Slot(() => Text("B")) },
                new Tab { Value = "c", ChildContent = new Slot(() => Text("C")) },
            }) },
            new TabPanel { Value = "a", ChildContent = new Slot(() => Text("Panel A")) },
            new TabPanel { Value = "b", ChildContent = new Slot(() => Text("Panel B")) },
            new TabPanel { Value = "c", ChildContent = new Slot(() => Text("Panel C")) },
        }),
    };

    [Fact]
    public void Compound_tabs_select_the_first_tab_and_link_tabs_to_panels()
    {
        using var r = TestRenderer.Render(NewTabs());
        var tabs = r.FindAll("[role=tab]");
        var panels = r.FindAll("[role=tabpanel]");

        Assert.Equal(["true", "false", "false"], tabs.Select(t => t.GetAttribute("aria-selected")));
        Assert.Equal(["0", "-1", "-1"], tabs.Select(t => t.GetAttribute("tabindex")));
        Assert.Equal(panels[1].GetAttribute("id"), tabs[1].GetAttribute("aria-controls"));
        Assert.Equal(tabs[1].GetAttribute("id"), panels[1].GetAttribute("aria-labelledby"));
        Assert.Equal([false, true, true], panels.Select(p => p.HasAttribute("hidden")));
    }

    [Fact]
    public void Compound_tabs_move_selection_and_focus_with_the_arrow_keys()
    {
        var changes = new List<string>();
        using var r = TestRenderer.Render(NewTabs(changes.Add));
        var tabs = r.FindAll("[role=tab]");

        r.KeyDown(tabs[0], "ArrowLeft");   // wraps round to the last tab

        Assert.Equal(tabs[2], r.Focused);
        Assert.Equal("active", tabs[2].GetAttribute("data-state"));
        Assert.Equal("Panel C", r.FindAll("[role=tabpanel]").Single(p => !p.HasAttribute("hidden")).Text);

        r.KeyDown(tabs[2], "Home");
        r.Click(tabs[1]);

        Assert.Equal(["c", "a", "b"], changes);
    }

    [Fact]
    public void Themed_wrapper_adds_its_class_to_the_headless_root_and_keeps_the_callers()
    {
        var open = new Signal<bool>(false);
        using var r = TestRenderer.Render(new Expander
        {
            Title = "Shipping",
            Open = open,
            Attributes = [Class("wide")],
            ChildContent = new Slot(() => P("Two days")),
        });
        var root = r.Find(".ui-expander");

        Assert.Equal("ui-expander wide", root.GetAttribute("class"));
        Assert.Equal("Shipping", r.Find(".ui-expander-title").Text);

        r.Click(r.Find("button"));

        Assert.True(open.Value);
        Assert.Equal("open", root.GetAttribute("data-state"));
    }
}
