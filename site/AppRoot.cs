using NetWasm.UI;
using NetWasm.UI.Routing;
using Site.Pages;
using static NetWasm.UI.Html;

namespace Site;

/// <summary>The site shell: header, routes and footer.</summary>
public sealed class AppRoot : Component
{
    public const string Repo = "https://github.com/matengo/NetWasm.UI";
    public const string Docs = Repo + "/blob/main/docs/";

    protected override Node Build() =>
        Div(
            Class("shell"),
            Header(
                Class("site-header"),
                Div(
                    Class("container header-inner"),
                    Link("/", Class("brand"), Span(Class("brand-mark"), "N"), Span("NetWasm", Span(Class("brand-dot"), ".UI"))),
                    Nav(
                        Class("site-nav"),
                        Attr("aria-label", "Main"),
                        NavLink("/examples", "active", "Examples"),
                        NavLink("/benchmarks", "active", "Benchmarks"),
                        External(Docs + "getting-started.md", "Docs"),
                        External(Repo, Class("nav-gh"), "GitHub ", Span(Class("version"), "v" + SiteInfo.Version))))),
            Main(
                Class("site-main"),
                Routes(
                    NotFound,
                    Route("/", () => new HomePage().Render()),
                    Route("/examples/*", () => new ExamplesPage().Render()),
                    Route("/benchmarks", () => new BenchmarksPage().Render()))),
            Footer(
                Class("site-footer"),
                Div(
                    Class("container footer-inner"),
                    P(
                        "This site is a NetWasm.UI app: C# compiled to WebAssembly. ",
                        External(Repo + "/tree/main/site", "View its source"), "."),
                    P(
                        Class("footer-links"),
                        External(Repo, "GitHub"),
                        External("https://www.nuget.org/packages/NwUi.Browser", "NuGet"),
                        External(Repo + "/blob/main/CHANGELOG.md", "Changelog"),
                        External("https://www.netwasm.com", "NetWasm"),
                        Span("MIT licensed")))));

    /// <summary>A link that leaves the site (the router ignores it) and opens in a new tab.</summary>
    public static Element External(string href, params Content[] content) =>
        A(Href(href), Attr("target", "_blank"), Attr("rel", "noopener"), Spread(content));

    public static void SetTitle(string title) =>
        SiteJs.SetTitle(title.Length == 0 ? "NetWasm.UI – Signal-driven C# UI at native Wasm size" : title + " – NetWasm.UI");

    private static Node NotFound()
    {
        SetTitle("Not found");
        return Section(
            Class("container page not-found"),
            H1("404"),
            P("Nothing lives at ", Html.Code(Text(() => Router.Current!.PathName)), "."),
            Link("/", Class("btn btn-primary"), "Back home"));
    }
}
