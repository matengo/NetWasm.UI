using System;
using NetWasm.UI;
using NetWasm.UI.Routing;
using Site.Code;
using Site.Demos;
using static NetWasm.UI.Html;

namespace Site.Pages;

/// <summary>"/examples/*": a layout route with a sidebar and a nested route per demo.</summary>
public sealed class ExamplesPage : Component
{
    private sealed class Demo(string slug, string title, string blurb, string[] files, Func<Node> render)
    {
        public string Slug { get; } = slug;
        public string Title { get; } = title;
        public string Blurb { get; } = blurb;
        public string[] Files { get; } = files;
        public Func<Node> Render { get; } = render;
    }

    private static readonly Demo[] Demos =
    [
        new("counter", "Counter", "A [Reactive] property, a Computed and an @if block. Only the text nodes that read the count change.",
            ["CounterDemo"], () => new CounterDemo().Render()),
        new("todos", "Todos", "A keyed @foreach with two-way binding. Adding or toggling an item touches only that item's nodes.",
            ["TodoDemo"], () => new TodoDemo().Render()),
        new("form", "Form validation", "Fields with validators, accessible error messages and an async submit with a server-side error.",
            ["FormDemo"], () => new FormDemo().Render()),
        new("async", "Async search", "A Resource keyed on a signal: each keystroke cancels the running search and starts a new one.",
            ["AsyncDemo"], () => new AsyncDemo().Render()),
        new("components", "Components", "Reusable components with reactive props, a two-way signal and named slots.",
            ["ComponentsDemo", "Stepper", "Card"], () => new ComponentsDemo().Render()),
        new("splatting", "Attribute splatting", "class, aria-*, data-* and @on… handlers written on a component tag land on its element, and a wrapper forwards them with @attributes.",
            ["SplatDemo", "AppButton", "IconButton", "TextField"], () => new SplatDemo().Render()),
    ];

    // samples/Tsp, published next to the site (/tsp/) by .github/workflows/pages.yml. A plain <a>, not Link: it is
    // another app, so the browser loads it.
    private const string RouteOptimizer = "tsp/";

    protected override Node Build()
    {
        var routes = new Route[Demos.Length + 1];
        routes[0] = Route("/", Index);
        for (var i = 0; i < Demos.Length; i++)
        {
            var demo = Demos[i];
            routes[i + 1] = Route("/" + demo.Slug, () => ShowDemo(demo));
        }

        return Div(
            Class("container page examples"),
            El(
                "aside",
                Class("examples-nav"),
                P(Class("eyebrow"), "Examples"),
                Nav(
                    Attr("aria-label", "Examples"),
                    Link("/examples", Class(() => Router.Current!.IsActive("/examples", exact: true) ? "active" : null), "Overview"),
                    Spread(NavItems()),
                    A(Href(RouteOptimizer), "Route optimizer ↗"))),
            Div(Class("examples-main"), Routes(NotFound, routes)));
    }

    private static Content[] NavItems()
    {
        var items = new Content[Demos.Length];
        for (var i = 0; i < Demos.Length; i++)
        {
            items[i] = NavLink("/examples/" + Demos[i].Slug, "active", Demos[i].Title);
        }

        return items;
    }

    private static Node Index()
    {
        AppRoot.SetTitle("Examples");
        var cards = new Content[Demos.Length + 1];
        for (var i = 0; i < Demos.Length; i++)
        {
            var demo = Demos[i];
            cards[i] = Link("/examples/" + demo.Slug, Class("example-card"), H3(demo.Title), P(demo.Blurb), Span(Class("feature-more"), "Try it →"));
        }

                    cards[^1] = A(
                        Href(RouteOptimizer),
                        Class("example-card"),
                        H3("Route optimizer"),
                        P("A traveling-salesman solver in C# Web Workers (NwUi.Workers): a pool of workers streams shorter and shorter tours of up to 10,000 points while the page stays responsive. A separate app."),
                        Span(Class("feature-more"), "Open it ↗"));
        return Div(
            HomePage.SectionHead(
                "Live examples",
                "Real components, running in your browser.",
                "Every example below is a .nwui component compiled into this site's Wasm. The source shown next to it is the actual file, fetched with a Resource inside Suspense."),
            Div(Class("example-grid"), Spread(cards)));
    }

    private static Node ShowDemo(Demo demo)
    {
        AppRoot.SetTitle(demo.Title + " example");
        var sources = new Content[demo.Files.Length];
        for (var i = 0; i < demo.Files.Length; i++)
        {
            var file = demo.Files[i];
            sources[i] = new SourceView { Path = "source/" + file + ".nwui.txt", Lang = "nwui", Caption = file + ".nwui" }.Render();
        }

        return Div(
            Class("example"),
            P(Class("eyebrow"), "Example"),
            H1(demo.Title),
            P(Class("lead"), demo.Blurb),
            Div(
                Class("example-split"),
                Div(Class("live"), Div(Class("live-label"), Span(Class("pulse")), "Live"), Div(Class("live-body"), demo.Render())),
                Div(Class("sources"), Spread(sources))));
    }

    private static Node NotFound() =>
        Div(H1("Unknown example"), P(Link("/examples", "See all examples")));
}
