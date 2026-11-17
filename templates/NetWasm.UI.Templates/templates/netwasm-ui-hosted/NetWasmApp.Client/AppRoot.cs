using NetWasmApp.Client.Pages;
using NetWasm.UI;
using NetWasm.UI.Routing;
using static NetWasm.UI.Html;

namespace NetWasmApp.Client;

/// <summary>The layout: header with navigation, routes and footer.</summary>
public sealed class AppRoot : Component
{
    protected override Node Build() =>
        Div(
            Class("shell"),
            Header(
                Class("site-header"),
                Div(
                    Class("container header-inner"),
                    Link("/", Class("brand"), Span(Class("brand-mark"), "N"), Span("NetWasmApp")),
                    Nav(
                        Class("site-nav"),
                        Attr("aria-label", "Main"),
                        NavLink("/", "active", "Home"),
                        NavLink("/counter", "active", "Counter"),
                        NavLink("/weather", "active", "Weather")))),
            Main(
                Class("site-main"),
                Routes(
                    NotFound,
                    Route("/", () => new HomePage().Render()),
                    Route("/counter", () => new CounterPage().Render()),
                    Route("/weather", () => new WeatherPage().Render()))),
            Footer(
                Class("site-footer"),
                Div(
                    Class("container footer-inner"),
                    P("A NetWasm.UI client hosted by ASP.NET Core on the same origin."),
                    P(
                        Class("footer-links"),
                        A(Href("https://github.com/matengo/NetWasm.UI"), "NetWasm.UI")))));

    private static Node NotFound() =>
        Section(
            Class("container page not-found"),
            H1("404"),
            P("Nothing lives at ", Code(Text(() => Router.Current!.PathName)), "."),
            Link("/", Class("btn btn-primary"), "Back home"));
}
