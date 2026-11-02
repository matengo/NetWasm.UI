using NetWasm.UI.Dom;
using NetWasm.UI.Reactive;
using static NetWasm.UI.Html;

namespace NetWasm.UI.Tests;

public class HtmlTemplateTests : RendererTest
{
    private static readonly Template Card = new(
        "<div class=\"card\"><h2><!--$--></h2><button $ type=\"button\">+</button><!--@--><p>static</p></div>");

    private sealed class Host : Component
    {
        private readonly Func<Node> build;

        public Host(Func<Node> build) => this.build = build;

        protected override Node Build() => build();
    }

    [Fact]
    public void Counts_root_and_holes_in_document_order()
    {
        Assert.Equal(4, Card.HoleCount);
        Assert.Equal(1, new Template("<p>no holes</p>").HoleCount);
    }

    [Fact]
    public void Ignores_markers_inside_attribute_values_and_on_the_root()
    {
        var t = new Template("<div $ title=\"<!--$--> $\"><span data-x='$'>a</span><i $></i></div>");
        Assert.Equal(2, t.HoleCount);
    }

    [Fact]
    public void Clone_renders_static_html_and_fills_holes()
    {
        var title = new Signal<string>("Hello");
        App.Mount(new Host(() => Card.Clone()
            .Text(1, () => title.Value)
            .With(2, Class("primary"))
            .With(3, Span("inserted"))
            .Root), "#app");

        Assert.Equal("<div class=\"card\"><h2>Hello</h2><button class=\"primary\" type=\"button\">+</button><span>inserted</span><!----><p>static</p></div>", Dom.Html);

        title.Value = "World";
        Assert.Contains("<h2>World</h2>", Dom.Html);
    }

    [Fact]
    public void Defines_the_template_once_and_clones_it_per_use()
    {
        App.Mount(new Host(() => Div(
            Card.Clone().Text(1, "a").Root,
            Card.Clone().Text(1, "b").Root,
            Card.Clone().Text(1, "c").Root)), "#app");

        Assert.Equal(1, Dom.Log.Count(l => l.Op == "DefineTemplate"));
        Assert.Equal(3, Dom.Log.Count(l => l.Op == "CloneTemplate"));
        Assert.Equal(0, Dom.Log.Count(l => l.Op == "CreateElement" && l.Detail.EndsWith(" h2")));
    }

    [Fact]
    public void Element_holes_take_listeners()
    {
        var clicks = 0;
        App.Mount(new Host(() => Card.Clone().With(2, OnClick(() => clicks++)).Root), "#app");

        Fire(Dom.Query("button")!, "click");
        Fire(Dom.Query("button")!, "click");

        Assert.Equal(2, clicks);
    }

    [Fact]
    public void Insertion_points_keep_reactive_content_in_place()
    {
        var on = new Signal<bool>(false);
        var items = new Signal<IReadOnlyList<int>>(new[] { 1, 2 });
        var t = new Template("<ul><li>first</li><!--@--><!--@--><li>last</li></ul>");
        App.Mount(new Host(() => t.Clone()
            .With(1, When(() => on.Value, () => Li("shown")))
            .With(2, Each(items, i => i, i => Li(i.ToString())))
            .Root), "#app");

        Assert.Equal("<ul><li>first</li><!----><!----><li>1</li><li>2</li><!----><!----><li>last</li></ul>", Dom.Html);

        on.Value = true;
        items.Value = new[] { 2, 3 };

        Assert.Equal("<ul><li>first</li><li>shown</li><!----><!----><li>2</li><li>3</li><!----><!----><li>last</li></ul>", Dom.Html);
    }

    [Fact]
    public void Ref_hands_out_the_element()
    {
        Element? button = null;
        App.Mount(new Host(() => Card.Clone().Ref(2, out button).Root), "#app");

        Assert.NotNull(button);
        Assert.Equal("button", Dom[button!.Id].Tag);
    }

    [Fact]
    public void Wrong_hole_kind_throws()
    {
        App.Mount(new Host(() =>
        {
            var c = Card.Clone();
            Assert.Throws<ArgumentOutOfRangeException>(() => c.Text(2, "x"));
            Assert.Throws<ArgumentOutOfRangeException>(() => c.With(1, "x"));
            Assert.Throws<ArgumentOutOfRangeException>(() => c.Element(3));
            Assert.Throws<ArgumentOutOfRangeException>(() => c.Element(4));
            return c.Root;
        }), "#app");
    }

    [Fact]
    public void Removing_a_branch_releases_its_holes()
    {
        var show = new Signal<bool>(true);
        App.Mount(new Host(() => Div(When(() => show.Value, () => Card.Clone().Text(1, "x").Root))), "#app");
        var before = Dom.Nodes.Count;

        show.Value = false;

        Assert.Equal(before - Card.HoleCount, Dom.Nodes.Count);
        Assert.DoesNotContain("card", Dom.Html);
    }

    [Fact]
    public void Is_defined_again_for_a_new_host()
    {
        App.Mount(new Host(() => Card.Clone().Root), "#app");
        Renderer.Reset();
        var dom = new FakeDomHost();
        Renderer.Initialize(dom);

        App.Mount(new Host(() => Card.Clone().Root), "#app");

        Assert.Equal(1, dom.Log.Count(l => l.Op == "DefineTemplate"));
    }

    [Fact]
    public void Rejects_html_without_a_root_element()
    {
        Assert.Throws<ArgumentException>(() => new Template("just text"));
        Assert.Throws<ArgumentException>(() => new Template("<!--$--><p></p>"));
    }
}
