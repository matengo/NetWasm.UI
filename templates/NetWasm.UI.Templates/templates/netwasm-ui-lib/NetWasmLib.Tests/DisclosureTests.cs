using NetWasm.UI;
using NetWasm.UI.Reactive;
using NetWasm.UI.Testing;
using static NetWasm.UI.Html;

namespace NetWasmLib.Tests;

public class DisclosureTests
{
    private static Disclosure NewDisclosure() => new()
    {
        Trigger = new Slot(() => Text("More")),
        ChildContent = new Slot(() => P("Details")),
    };

    [Fact]
    public void Starts_closed_and_links_the_trigger_to_the_panel()
    {
        using var r = TestRenderer.Render(NewDisclosure());
        var trigger = r.Find("button");
        var panel = r.Find("[role=region]");

        Assert.Equal("false", trigger.GetAttribute("aria-expanded"));
        Assert.Equal(panel.GetAttribute("id"), trigger.GetAttribute("aria-controls"));
        Assert.Equal(trigger.GetAttribute("id"), panel.GetAttribute("aria-labelledby"));
        Assert.True(panel.HasAttribute("hidden"));
    }

    [Fact]
    public void Clicking_the_trigger_opens_the_panel_and_reports_the_change()
    {
        var changes = new List<bool>();
        var disclosure = NewDisclosure();
        disclosure.OnOpenChange = changes.Add;
        using var r = TestRenderer.Render(disclosure);

        r.Click(r.Find("button"));

        Assert.Equal("true", r.Find("button").GetAttribute("aria-expanded"));
        Assert.Equal("open", r.Find("[role=region]").GetAttribute("data-state"));
        Assert.False(r.Find("[role=region]").HasAttribute("hidden"));
        Assert.Equal([true], changes);
    }

    [Fact]
    public void A_controlled_disclosure_follows_the_callers_signal()
    {
        var open = new Signal<bool>(false);
        var disclosure = NewDisclosure();
        disclosure.Open = open;
        using var r = TestRenderer.Render(disclosure);

        open.Value = true;
        Assert.Equal("true", r.Find("button").GetAttribute("aria-expanded"));

        r.Click(r.Find("button"));
        Assert.False(open.Value);
    }

    [Fact]
    public void The_callers_attributes_land_on_the_root()
    {
        var disclosure = NewDisclosure();
        disclosure.Attributes = [Class("faq"), Attr("data-testid", "faq")];
        using var r = TestRenderer.Render(disclosure);

        Assert.Equal("faq", r.Find("[data-testid=faq]").GetAttribute("class"));
    }
}
