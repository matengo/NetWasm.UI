using NetWasm.UI.Dom;
using NetWasm.UI.Reactive;
using NetWasm.UI.Tests.Templates;
using static NetWasm.UI.Html;

namespace NetWasm.UI.Tests;

public class TestRendererTests
{
    private sealed class Lambda(Func<Node> build) : Component
    {
        protected override Node Build() => build();
    }

    [Fact]
    public void Finds_and_clicks_through_a_generated_component()
    {
        using var r = TestRenderer.Render(new Clicker());
        var button = r.Find("p.clicker button");
        Assert.Equal("Clicks: 1", button.Text);
        Assert.Empty(r.FindAll("strong"));

        r.Click(button);

        Assert.Equal("Clicks: 2", button.Text);
        Assert.Equal("busy", r.Find(".clicker strong").Text);
    }

    [Fact]
    public void Row_events_reach_the_row_of_the_target()
    {
        var view = new RowEvents();
        using var r = TestRenderer.Render(view);
        view.Entries.Value = [new(1, "Ada"), new(2, "Bo")];

        r.Click(r.FindAll("a.pick")[1]);
        r.KeyDown(r.Find("li[data-id=\"2\"] .key"), "Enter");

        Assert.Equal("on", r.Find("li[data-id=2]").GetAttribute("class"));
        Assert.Null(r.Find("li[data-id=1]").GetAttribute("class"));
        Assert.Equal(["BoEnter"], view.Keys);
    }

    [Fact]
    public void Events_capture_down_then_bubble_up_until_stopped()
    {
        var log = new List<string>();
        using var r = TestRenderer.Render(new Lambda(() =>
            Div(
                Attr("id", "outer"),
                On("click", () => log.Add("outer")),
                On("click", () => log.Add("capture"), ListenerFlags.Capture),
                Button(Attr("id", "go"), On("click", () => log.Add("go"))),
                Button(Attr("id", "stop"), On("click", () => log.Add("stop"), ListenerFlags.StopPropagation)))));

        r.Click(r.Find("#go"));
        Assert.Equal(["capture", "go", "outer"], log);

        log.Clear();
        r.Click(r.Find("#stop"));
        Assert.Equal(["capture", "stop"], log);
    }

    [Fact]
    public void Input_focus_click_outside_and_resize()
    {
        var typed = "";
        bool? inside = null;
        double width = 0;
        using var r = TestRenderer.Render(new Lambda(() =>
        {
            var panel = Div(
                Attr("id", "panel"),
                Input(Attr("id", "name"), On("input", e => typed = e.Value)),
                Button(Attr("id", "inner"), "x"));
            OnDocument("click", e => inside = e.Inside, within: panel);
            OnResize(panel, rect => width = rect.Width);
            OnMount(panel.FocusFirst);
            return Div(panel, Button(Attr("id", "outside"), "o"));
        }));

        Assert.Equal("name", r.Focused?.GetAttribute("id"));

        r.Input(r.Find("#name"), "Ada");
        Assert.Equal("Ada", typed);
        Assert.Equal("Ada", r.Find("#name").Value);

        r.Click(r.Find("#inner"));
        Assert.True(inside);
        r.Click(r.Find("#outside"));
        Assert.False(inside);

        r.SetRect(r.Find("#panel"), new Rect(0, 0, 200, 50));
        Assert.Equal(200, width);
    }

    [Fact]
    public void Removed_elements_disconnect_and_misses_throw()
    {
        var visible = new Signal<bool>(true);
        using (var r = TestRenderer.Render(new Lambda(() => Div(When(() => visible.Value, () => P("x")).AnimateExit()))))
        {
            var p = r.Find("div p");
            Assert.Equal("div", p.Parent?.Tag);
            visible.Value = false;

            Assert.False(p.IsConnected);
            Assert.Throws<InvalidOperationException>(() => r.Click(p));
            Assert.Contains("<div><!----></div>", Assert.Throws<InvalidOperationException>(() => r.Find("p")).Message);
            Assert.Throws<FormatException>(() => r.Find("div > p"));
        }

        // Disposing frees the global renderer for the next one.
        using var next = TestRenderer.Render(new Lambda(() => P("again")));
        Assert.Equal("<p>again</p>", next.Html);
    }

    [Fact]
    public void Netstandard_library_component_traps_and_restores_focus()
    {
        // NwUi.Primitives targets netstandard2.1, so this net10.0 project consumes it unchanged.
        var open = new Signal<bool>(false);
        using var r = TestRenderer.Render(new Lambda(() => Div(
            Button(Attr("id", "opener"), On("click", () => open.Value = true), "open"),
            When(() => open.Value, () => Div(new NetWasm.UI.Primitives.FocusScope
            {
                Trap = true,
                ChildContent = new Slot(() => new Content[]
                {
                    Button(Attr("id", "first"), "first"),
                    Button(Attr("id", "close"), On("click", () => open.Value = false), "close"),
                }),
            })))));

        var opener = r.Find("#opener");
        r.Focus(opener);
        r.Click(opener);
        Assert.Equal("first", r.Focused?.GetAttribute("id"));

        r.Focus(opener);
        Assert.Equal("first", r.Focused?.GetAttribute("id"));

        r.Click(r.Find("#close"));
        Assert.Equal("opener", r.Focused?.GetAttribute("id"));
    }
}
