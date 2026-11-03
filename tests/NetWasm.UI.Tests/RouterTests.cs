using NetWasm.UI.Reactive;
using NetWasm.UI.Routing;
using static NetWasm.UI.Html;

namespace NetWasm.UI.Tests;

public class RoutePatternTests
{
    [Theory]
    [InlineData("/", "/", true)]
    [InlineData("/", "", true)]
    [InlineData("/", "/x", false)]
    [InlineData("/todos", "/todos", true)]
    [InlineData("/todos", "/Todos/", true)]
    [InlineData("/todos", "/todos?done=1#top", true)]
    [InlineData("/todos", "/todos/3", false)]
    [InlineData("/todos/{id}", "/todos", false)]
    [InlineData("/todos/{id}", "/todos/3", true)]
    [InlineData("/todos/{id}", "/todos/3/edit", false)]
    [InlineData("/files/{*path}", "/files", true)]
    [InlineData("/files/{*path}", "/files/a/b/c", true)]
    public void Matches(string pattern, string path, bool expected)
    {
        Assert.Equal(expected, new RoutePattern(pattern).TryMatch(path, out _));
    }

    [Fact]
    public void Captures_decoded_parameters_and_query()
    {
        Assert.True(new RoutePattern("/greet/{name}/{n}").TryMatch("/greet/Ada%20L/42?x=1#h", out var p));

        Assert.Equal(2, p.Count);
        Assert.Equal("Ada L", p["name"]);
        Assert.Equal(42, p.GetInt("n"));
        Assert.Equal("x=1", p.Query);
        Assert.Null(p["missing"]);
    }

    [Theory]
    [InlineData("abc-._~XYZ09", "abc-._~XYZ09")]
    [InlineData("Ada L", "Ada%20L")]
    [InlineData("Åsa & Ölund/?#%", "%C3%85sa%20%26%20%C3%96lund%2F%3F%23%25")]
    [InlineData("😀", "%F0%9F%98%80")]
    [InlineData("", "")]
    public void Url_encodes_like_Uri_and_round_trips(string raw, string encoded)
    {
        Assert.Equal(encoded, Url.Encode(raw));
        Assert.Equal(Uri.EscapeDataString(raw), Url.Encode(raw));
        Assert.Equal(raw, Url.Decode(encoded));
    }

    [Theory]
    [InlineData("a%2", "a%2")]
    [InlineData("%zz%41", "%zzA")]
    [InlineData("100%", "100%")]
    [InlineData("%c3%a5", "å")]
    public void Url_decode_keeps_malformed_escapes(string encoded, string expected)
    {
        Assert.Equal(expected, Url.Decode(encoded));
        Assert.Equal(Uri.UnescapeDataString(encoded), Url.Decode(encoded));
    }

    [Fact]
    public void Catch_all_joins_remaining_segments()
    {
        Assert.True(new RoutePattern("/files/{*path}").TryMatch("/files/a/b%2Fc/d", out var p));
        Assert.Equal("a/b/c/d", p["path"]);
    }

    [Fact]
    public void Catch_all_must_be_last()
    {
        Assert.Throws<ArgumentException>(() => new RoutePattern("/{*rest}/x"));
    }
}

public class RouterTests : RendererTest
{
    private readonly MemoryHistory history = new("/");
    private readonly Router router;

    public RouterTests()
    {
        router = Router.Install(history);
    }

    public override void Dispose()
    {
        Router.Reset();
        base.Dispose();
    }

    private Node Shell() =>
        Div(
            Nav(NavLink("/", "active", "Home"), NavLink("/todos", "active", "Todos")),
            Routes(
                () => P("404"),
                Route("/", () => H1("Home")),
                Route("/todos", () => H1("Todos")),
                Route("/todos/{id}", p => H1(Text(() => $"Todo {p.Get("id")}")))));

    [Fact]
    public void Renders_matching_route_and_active_link()
    {
        App.Mount(new Lambda(Shell), "#app");

        Assert.Equal(
            "<div><nav><a class=\"active\" href=\"/\">Home</a><a href=\"/todos\">Todos</a></nav><h1>Home</h1><!----></div>",
            Dom.Html);
    }

    [Fact]
    public void Plain_click_on_link_navigates_and_pushes_history()
    {
        App.Mount(new Lambda(Shell), "#app");

        Fire(Dom.Query("a", a => a.Attributes["href"] == "/todos")!, new DomEvent("click"));

        Assert.Equal("/todos", history.Path);
        Assert.Equal("/todos", router.Path.Value);
        Assert.Contains("<h1>Todos</h1>", Dom.Html);
        Assert.Contains("<a href=\"/\">Home</a><a class=\"active\" href=\"/todos\">Todos</a>", Dom.Html);
    }

    [Fact]
    public void Modified_click_is_left_to_the_browser()
    {
        App.Mount(new Lambda(Shell), "#app");

        Fire(Dom.Query("a", a => a.Attributes["href"] == "/todos")!, new DomEvent("click", modifiers: EventModifiers.Ctrl));
        Fire(Dom.Query("a", a => a.Attributes["href"] == "/todos")!, new DomEvent("click", button: 1));

        Assert.Equal("/", history.Path);
        Assert.Contains("<h1>Home</h1>", Dom.Html);
    }

    [Fact]
    public void Back_button_restores_previous_page()
    {
        App.Mount(new Lambda(Shell), "#app");
        router.Navigate("/todos/7");
        Assert.Contains("<h1>Todo 7</h1>", Dom.Html);

        history.Back();

        Assert.Contains("<h1>Home</h1>", Dom.Html);
        Assert.DoesNotContain("Todo 7", Dom.Html);
    }

    [Fact]
    public void Parameter_change_rebuilds_page_and_same_match_does_not()
    {
        App.Mount(new Lambda(Shell), "#app");
        router.Navigate("/todos/1");
        var h1 = Dom.Query("h1")!;

        router.Navigate("/todos/1#section");
        Assert.Same(h1, Dom.Query("h1"));           // same route, same params (hash ignored): no rebuild

        router.Navigate("/todos/2");
        Assert.NotSame(h1, Dom.Query("h1"));
        Assert.Contains("<h1>Todo 2</h1>", Dom.Html);
    }

    [Fact]
    public void Unmatched_path_renders_not_found_and_releases_old_page()
    {
        App.Mount(new Lambda(Shell), "#app");
        var nodesBefore = Dom.Nodes.Count;

        router.Navigate("/nope");

        Assert.Contains("<p>404</p>", Dom.Html);
        Assert.DoesNotContain("<h1>", Dom.Html);
        Assert.Equal(nodesBefore, Dom.Nodes.Count); // <h1>Home</h1> (2 nodes) swapped for <p>404</p> (2 nodes)
    }

    [Fact]
    public void Navigation_is_one_batch()
    {
        App.Mount(new Lambda(Shell), "#app");
        var before = Dom.BatchCount;

        Scheduler.Batch(() => router.Navigate("/todos"));

        Assert.Equal(before + 1, Dom.BatchCount);
    }

    [Fact]
    public void IsActive_prefix_semantics()
    {
        router.Navigate("/todos/3");

        Assert.True(router.IsActive("/todos"));
        Assert.False(router.IsActive("/todos", exact: true));
        Assert.False(router.IsActive("/todo"));
        Assert.False(router.IsActive("/"));
    }

    private Node Settings()
    {
        var builds = 0;
        return Div(
            Class("settings"),
            Text(() => "layout " + (++builds).ToString()),
            Routes(
                () => P("no such setting"),
                Route("/", () => H2("Overview")),
                Route("/profile", () => H2("Profile")),
                Route("/users/{id}", p => H2("User " + p.Get("id")))));
    }

    private Node NestedShell() =>
        Main(Routes(
            () => P("404"),
            Route("/", () => H1("Home")),
            Route("/settings/*", Settings)));

    [Fact]
    public void Nested_routes_match_relative_to_the_prefix_and_keep_the_layout()
    {
        App.Mount(new Lambda(NestedShell), "#app");
        router.Navigate("/settings");
        var layout = Dom.Query("div")!;
        Assert.Contains("<h2>Overview</h2>", Dom.Html);

        router.Navigate("/settings/profile");
        Assert.Same(layout, Dom.Query("div"));
        Assert.Contains("<h2>Profile</h2>", Dom.Html);
        Assert.Contains("layout 1", Dom.Html);

        router.Navigate("/settings/users/7?tab=x");
        Assert.Contains("<h2>User 7</h2>", Dom.Html);

        router.Navigate("/settings/nope");
        Assert.Contains("<p>no such setting</p>", Dom.Html);
        Assert.Same(layout, Dom.Query("div"));

        router.Navigate("/");
        Assert.Contains("<h1>Home</h1>", Dom.Html);
        Assert.DoesNotContain("settings", Dom.Html);
    }

    [Theory]
    [InlineData("/settings/*", "/settings", "/")]
    [InlineData("/settings/*", "/settings/a/b?x=1#h", "/a/b?x=1")]
    [InlineData("/org/{org}/*", "/org/acme/repos", "/repos")]
    public void Prefix_pattern_returns_the_rest(string pattern, string path, string rest)
    {
        Assert.True(new RoutePattern(pattern).TryMatch(path, out _, out var actual));
        Assert.Equal(rest, actual);
        Assert.False(new RoutePattern(pattern).TryMatch("/other", out _));
    }

    private sealed class Lambda : Component
    {
        private readonly Func<Node> build;
        public Lambda(Func<Node> build) => this.build = build;
        protected override Node Build() => build();
    }
}

public class RouterBasePathTests : RendererTest
{
    private readonly MemoryHistory history = new("/app/todos?x=1");
    private readonly Router router;

    public RouterBasePathTests()
    {
        router = Router.Install(history, "/app/");
    }

    public override void Dispose()
    {
        Router.Reset();
        base.Dispose();
    }

    [Fact]
    public void Paths_are_app_relative_and_history_gets_the_base()
    {
        Assert.Equal("/app", router.BasePath);
        Assert.Equal("/todos?x=1", router.Path.Value);

        router.Navigate("/");
        Assert.Equal("/app/", history.Path);
        Assert.Equal("/", router.Path.Value);

        history.Back();
        Assert.Equal("/todos?x=1", router.Path.Value);
    }

    [Theory]
    [InlineData("/app", "/")]
    [InlineData("/app/", "/")]
    [InlineData("/app?q=1", "/?q=1")]
    [InlineData("/application", "/application")]
    [InlineData("/other", "/other")]
    public void Browser_paths_are_mapped(string browserPath, string appPath)
    {
        Router.Install(new MemoryHistory(browserPath), "app");
        Assert.Equal(appPath, Router.Current!.Path.Value);
    }

    [Fact]
    public void Links_render_with_the_base_and_navigate_app_relative()
    {
        App.Mount(new Lambda(() => Div(Link("/todos/3", "Todo"), Link("https://example.com/", "Out"), Routes(Route("/todos/{id}", p => P(p.Get("id")))))), "#app");

        var link = Dom.Query("a")!;
        Assert.Equal("/app/todos/3", link.Attributes["href"]);
        Assert.Equal("https://example.com/", Dom.QueryAll("a")[1].Attributes["href"]);

        Fire(link, new DomEvent("click"));
        Assert.Equal("/app/todos/3", history.Path);
        Assert.Contains("<p>3</p>", Dom.Html);
    }

    [Fact]
    public void Root_base_changes_nothing()
    {
        var r = Router.Install(new MemoryHistory("/x"), "/");
        Assert.Equal(string.Empty, r.BasePath);
        Assert.Equal("/x", r.Href("/x"));
    }

    private sealed class Lambda : Component
    {
        private readonly Func<Node> build;
        public Lambda(Func<Node> build) => this.build = build;
        protected override Node Build() => build();
    }
}
