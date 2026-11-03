using NetWasm.UI;
using NetWasm.UI.Routing;
using NetWasmApp.Pages;
using static NetWasm.UI.Html;

namespace NetWasmApp;

/// <summary>The root component: a header with navigation and the routed page.</summary>
public sealed class App : Component
{
    protected override Node Build() =>
        Div(
            Class("layout"),
            Header(
                Strong(Class("brand"), "NetWasmApp"),
                Nav(
                    NavLink("/", "active", "Home"),
                    NavLink("/counter", "active", "Counter"))),
            Main(
                Routes(
                    NotFound,
                    Route("/", () => new HomePage().Render()),
                    Route("/counter", () => new CounterPage().Render()))));

    private static Node NotFound() =>
        Section(
            H1("Not found"),
            P("Nothing lives at ", Code(Text(() => Router.Current!.PathName)), "."),
            Link("/", "Go home"));
}
