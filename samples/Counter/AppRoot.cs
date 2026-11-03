using NetWasm.UI;
using NetWasm.UI.Reactive;
using NetWasm.UI.Routing;
using static NetWasm.UI.Html;

namespace Counter;

public sealed class AppRoot : Component
{
    // Pages are rebuilt on each navigation (Routes disposes the previous page's scope), so
    // state that should survive navigation lives in the AppState store provided here.
    private readonly AppState state = new();

    protected override Node Build()
    {
        Provide(AppState.Context, state);
        return Main(
            Header(
                H1("NetWasm.UI"),
                Nav(
                    NavLink("/", "active", "Home"),
                    NavLink("/counter", "active", "Counter"),
                    NavLink("/todos", "active", "Todos"),
                    NavLink("/components", "active", "Components"),
                    NavLink("/signup", "active", "Sign up"),
                    NavLink("/settings", "active", "Settings"),
                    NavLink("/greet/world", "active", "Greet"))),
            Routes(
                NotFound,
                Route("/", Home),
                Route("/counter", () => new CounterView().Render()),
                Route("/todos", () => new TodoList().Render()),
                Route("/components", () => new ComponentsPage().Render()),
                Route("/signup", () => new SignupPage().Render()),
                Route("/settings/*", Settings),
                Route("/greet/{name}", Greet)));
    }

    private static Node Home() =>
        Section(
            P(Class("tagline"), "Reflection-free, signal-driven UI compiled with NetWasm."),
            P("This sample is a small multi-page app. Links are intercepted client-side, ",
              "ctrl/middle-click still opens a new tab, and the back button works."),
            P(Link("/todos/3", "A link to a route that does not exist"), " shows the 404 page."));

    private static int settingsBuilds;

    // A layout route: "/settings/*" stays mounted while the nested Routes switch on the rest of the path.
    private static Node Settings()
    {
        var layoutBuild = (++settingsBuilds).ToString();
        return Section(
            Id("settings"),
            Attr("data-build", layoutBuild),
            H2("Settings"),
            Nav(
                Class("subnav"),
                Link("/settings", Class(() => Router.Current!.IsActive("/settings", exact: true) ? "active" : null), "Overview"),
                NavLink("/settings/profile", "active", "Profile"),
                NavLink("/settings/security", "active", "Security")),
            Routes(
                () => P(Id("settings-page"), "Unknown setting."),
                Route("/", () => P(Id("settings-page"), "Pick a section.")),
                Route("/profile", () => P(Id("settings-page"), "Profile settings.")),
                Route("/security", () => P(Id("settings-page"), "Security settings."))));
    }

    private static Node Greet(RouteParams p)
    {
        var greetDraft = Use(AppState.Context).GreetDraft;
        return Section(
            H2(Text(() => $"Hello, {p.Get("name")}!")),
            P(Class("status"), Text(() => $"Query: '{p.Query}'")),
            Label("Change name: ", Input(Id("greet-name"), Spread(Bind(greetDraft)))),
            " ",
            Button(Id("greet-go"), OnClick(() => Router.Current!.Navigate("/greet/" + Url.Encode(greetDraft.Value))), "Go"));
    }

    private static Node NotFound() =>
        Section(
            H2("404"),
            P("No route matches ", Code(Text(() => Router.Current!.PathName)), "."),
            Link("/", "Back home"));
}
