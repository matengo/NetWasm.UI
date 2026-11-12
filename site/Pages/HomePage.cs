using NetWasm.UI;
using Site.Code;
using static NetWasm.UI.Html;

namespace Site.Pages;

public sealed class HomePage : Component
{
    private const string Quickstart = """
        dotnet new install NwUi.Templates
        dotnet new netwasm-ui -o MyApp
        cd MyApp
        dotnet nwui-dev            # http://localhost:8765, reloads on save and keeps your state
        """;

    private const string TemplateSample = """
        @* CounterView.nwui → public partial class CounterView : Component *@
        <section>
            <h2>Counter</h2>
            <p>Count: <strong>@count</strong> &middot; doubled: @doubled</p>
            <button @onclick="() => count.Value++">+1</button>
            <button disabled="@(count.Value == 0)" @onclick="() => count.Value = 0">Reset</button>
            @if (count.Value >= 10) {
                <p class="hint">That's a lot of clicks!</p>
            }
        </section>

        @code {
            private readonly Signal<int> count = new(0);
            private Computed<int> doubled = null!;

            partial void OnInit() => doubled = new Computed<int>(() => count.Value * 2);
        }
        """;

    private const string CSharpSample = """
        public sealed class CounterView : Component
        {
            private readonly Signal<int> count = new(0);

            protected override Node Build()
            {
                var doubled = new Computed<int>(() => count.Value * 2);

                return Section(
                    H2("Counter"),
                    P("Count: ", Strong(Text(count)), " · doubled: ", Text(doubled)),
                    Button(OnClick(() => count.Value++), "+1"),
                    Button(Disabled(() => count.Value == 0), OnClick(() => count.Value = 0), "Reset"),
                    When(() => count.Value >= 10, () => P("That's a lot of clicks!")));
            }
        }
        """;

    protected override Node Build()
    {
        AppRoot.SetTitle("");
        return Div(
            Class("home"),
            Hero(),
            Stats(),
            TwoWays(),
            Pipeline(),
            Features(),
            Speed(),
            Packages(),
            GetStarted());
    }

    private static Node Hero() =>
        Section(
            Class("hero"),
            Div(
                Class("container hero-inner"),
                Div(
                    Class("hero-copy"),
                    P(Class("eyebrow"), "C# · WebAssembly · Signals"),
                    H1("Signal-driven C# UI.", Br(), Span(Class("grad"), "At native Wasm size.")),
                    P(
                        Class("lead"),
                        "NetWasm.UI is a reflection-free UI framework for the browser, compiled ahead-of-time to ",
                        "WebAssembly with NetWasm. Inspired by Solid and Svelte rather than Blazor: components build ",
                        "their DOM once, fine-grained signals patch exactly the nodes that changed, and every state ",
                        "change crosses the Wasm→JS boundary at most once."),
                    Div(
                        Class("cta"),
                        AppRoot.External(AppRoot.Docs + "getting-started.md", Class("btn btn-primary btn-lg"), "Get started"),
                        Link("/examples", Class("btn btn-lg"), "Live examples →")),
                    new SizeBadge().Render()),
                new HeroCounter().Render()));

    private static Node Stats() =>
        Section(
            Class("stats"),
            Div(
                Class("container stats-grid"),
                Stat("~160 KB", "a whole app with router and forms, Brotli, Wasm and JS together"),
                Stat("≤ 1", "Wasm→JS call per state change, however many nodes it touches"),
                Stat("0", "reflection, runtime template engines or virtual DOM"),
                Stat("3–21×", "faster than Blazor WebAssembly, with a ~12× smaller download")));

    private static Node Stat(string value, string label) =>
        Div(Class("stat"), Strong(value), Span(label));

    private static Node TwoWays() =>
        Section(
            Class("section"),
            Div(
                Class("container"),
                SectionHead(
                    "Write it like Svelte",
                    "HTML templates or plain C#. Same output.",
                    "Write components as .nwui templates, HTML with Razor-style @. A source generator turns them into C# at build time, so there is no template engine at runtime. Or skip the templates and call the builder API yourself; it is what the generator emits."),
                Div(
                    Class("code-pair"),
                    new CodeBlock { Source = TemplateSample, Lang = "nwui", Caption = "CounterView.nwui" }.Render(),
                    new CodeBlock { Source = CSharpSample, Lang = "cs", Caption = "CounterView.cs" }.Render())));

    private static Node Pipeline() =>
        Section(
            Class("section section-alt"),
            Div(
                Class("container"),
                SectionHead(
                    "How it works",
                    "No runtime. No reflection. One Wasm file.",
                    "Everything is decided at compile time. NetWasm compiles only the reachable code straight to Wasm, so an unused feature costs nothing."),
                Ol(
                    Class("pipeline"),
                    Step("01", ".nwui", "Your templates and C# components"),
                    Step("02", "C#", "The source generator emits plain component classes"),
                    Step("03", "Wasm", "NetWasm compiles the reachable program ahead of time"),
                    Step("04", "DOM", "Signals batch changes into one binary call to a tiny JS decoder"))));

    private static Node Step(string number, string title, string text) =>
        Li(Span(Class("step-num"), number), Strong(title), Span(text));

    private static Node Features() =>
        Section(
            Class("section"),
            Div(
                Class("container"),
                SectionHead(
                    "Batteries included",
                    "Everything an app needs, in a few hundred KB.",
                    "Each feature is ordinary C# the compiler can see, so you pay only for what you use."),
                Div(
                    Class("features"),
                    Feature("reactivity.md", "Fine-grained signals", "Signal, Computed and Effect with batching, context and stores, or [Reactive] properties you just read and write. Only the nodes that read a signal update."),
                    Feature("templates.md", ".nwui templates", "HTML with @if, @foreach, @bind, slots, props and components. Static subtrees are cloned, not rebuilt."),
                    Feature("routing.md", "Router", "Route parameters, nested layouts, NavLink, base paths and the History API. This site uses it."),
                    Feature("forms.md", "Forms and validation", "Fields with validators, touched and ARIA state, async submit and server errors."),
                    Feature("async.md", "Async and Suspense", "Resource<T> turns a Task into reactive state; Suspense and ErrorBoundary handle loading and failure."),
                    Feature("services.md", "Dependency injection", "Reflection-free Services, Inject<T>() and @inject, wired at compile time by a generator."),
                    Feature("js-interop.md", "JavaScript interop", "[JSImport], callbacks and Promises: wrap any JS library in a component."),
                    Feature("security.md", "Secure by default", "Text is never parsed as HTML. Works under a strict Content Security Policy and Trusted Types."))));

    private static Node Feature(string doc, string title, string text) =>
        AppRoot.External(AppRoot.Docs + doc, Class("feature"), H3(title), P(text), Span(Class("feature-more"), "Read the docs →"));

    private static Node Speed() =>
        Section(
            Class("section section-alt"),
            Div(
                Class("container speed"),
                Div(
                    SectionHead(
                        "Speed, measured",
                        "Level with React. Close to Solid.",
                        "In a js-framework-benchmark-style comparison NetWasm.UI is level with or faster than React on every case and between 0.9× and 1.6× SolidJS's time. Lower is better."),
                    Link("/benchmarks", Class("btn"), "All benchmark results →")),
                Div(
                    Class("bars"),
                    P(Class("bars-title"), "Create 1,000 rows, ms (script + layout)"),
                    Bar("NetWasm.UI", 57.0, 251.9, true),
                    Bar("SolidJS", 60.7, 251.9, false),
                    Bar("React", 77.1, 251.9, false),
                    Bar("Blazor WASM", 251.9, 251.9, false))));

    private static Node Bar(string name, double value, double max, bool highlight)
    {
        var percent = (int)(value / max * 100 + 0.5);
        return Div(
            Class(highlight ? "bar bar-hi" : "bar"),
            Span(Class("bar-name"), name),
            Div(Class("bar-track"), Div(Class("bar-fill"), Style("width:" + percent.ToString() + "%"))),
            Span(Class("bar-value"), Format(value)));
    }

    private static string Format(double value)
    {
        var tenths = (int)(value * 10 + 0.5);
        return (tenths / 10).ToString() + "." + (tenths % 10).ToString();
    }

    private static Node Packages() =>
        Section(
            Class("section"),
            Div(
                Class("container"),
                SectionHead(
                    "Packages",
                    "On NuGet, released together.",
                    "Package IDs start with NwUi because the NetWasm. prefix belongs to the NetWasm toolchain; the namespaces are NetWasm.UI."),
                Div(
                    Class("table-wrap"),
                    Table(
                        Class("table"),
                        Thead(Tr(Th("Package"), Th("What it is"))),
                        Tbody(
                            Package("NwUi.Browser", "The package an app references: the browser host and the DOM module. Brings in NwUi."),
                            Package("NwUi", "The core: signals, components, router, forms and the .nwui template compiler."),
                            Package("NwUi.Templates", "dotnet new netwasm-ui."),
                            Package("NwUi.DevServer", "dotnet nwui-dev: publishes, serves with Brotli and SPA fallback, and live-reloads in ~3 s, keeping signal and form state.")))),
                P(
                    Class("fine-print"),
                    "Requires the .NET SDK 10.0.3xx or 11 and a browser with Wasm exception handling (Chrome/Edge 137+, Firefox 131+, Safari 18.4+). ",
                    "NetWasm.UI is MIT. The NetWasm compiler uses the ",
                    AppRoot.External("https://github.com/zion-sati/NetWasm", "NetWasm Community License"),
                    "; the apps you build can use any license.")));

    private static Node Package(string id, string text) =>
        Tr(Td(AppRoot.External("https://www.nuget.org/packages/" + id, Html.Code(id))), Td(text));

    private static Node GetStarted() =>
        Section(
            Class("section get-started"),
            Div(
                Class("container narrow"),
                H2("Get started in 30 seconds"),
                P(Class("lead"), "Install the template, create an app and run the dev server."),
                new CodeBlock { Source = Quickstart, Lang = "sh", Caption = "Terminal" }.Render(),
                Div(
                    Class("cta cta-center"),
                    AppRoot.External(AppRoot.Docs + "getting-started.md", Class("btn btn-primary btn-lg"), "Read the guide"),
                    AppRoot.External(AppRoot.Repo, Class("btn btn-lg"), "Star on GitHub"))));

    public static Node SectionHead(string eyebrow, string title, string text) =>
        Div(Class("section-head"), P(Class("eyebrow"), eyebrow), H2(title), P(Class("lead"), text));
}
