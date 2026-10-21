using NetWasm.UI.Reactive;
using NetWasm.UI.Routing;
using static NetWasm.UI.Html;

namespace NetWasm.UI.Tests;

public class ContextTests : RendererTest
{
    private static readonly Context<string> Theme = new("Theme", "light");
    private static readonly Context<Store> StoreContext = new("Store");

    private sealed class Store
    {
        public Signal<int> Count { get; } = new(0);
    }

    [Fact]
    public void Use_returns_default_without_provider()
    {
        App.Mount(new Lambda(() => P(Use(Theme))), "#app");

        Assert.Equal("<p>light</p>", Dom.Html);
    }

    [Fact]
    public void Use_without_provider_or_default_throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => App.Mount(new Lambda(() => P(Use(StoreContext).Count.Peek().ToString())), "#app"));
        Assert.Contains("Store", ex.Message);
    }

    [Fact]
    public void Descendant_components_see_nearest_provider()
    {
        App.Mount(new Lambda(() =>
        {
            Provide(Theme, "dark");
            return Div(
                new Lambda(() => Span(Use(Theme))),
                new Lambda(() =>
                {
                    Provide(Theme, "blue");
                    return Div(new Lambda(() => Span(Use(Theme))));
                }),
                new Lambda(() => Span(Use(Theme))));
        }), "#app");

        Assert.Equal("<div><span>dark</span><div><span>blue</span></div><span>dark</span></div>", Dom.Html);
    }

    [Fact]
    public void Scoped_provide_does_not_leak_to_siblings()
    {
        App.Mount(new Lambda(() =>
            Div(
                Provide(Theme, "dark", () => Span(Use(Theme))),
                Span(Use(Theme)))), "#app");

        Assert.Equal("<div><span>dark</span><span>light</span></div>", Dom.Html);
    }

    [Fact]
    public void Context_is_visible_in_lazily_built_show_and_for_branches()
    {
        var visible = new Signal<bool>(false);
        var items = new Signal<IReadOnlyList<int>>(new List<int>());

        App.Mount(new Lambda(() =>
        {
            Provide(Theme, "dark");
            return Div(
                When(() => visible.Value, () => new Lambda(() => B(Use(Theme))).Render()),
                Ul(Each(items, i => i, i => Li(Use(Theme) + i))));
        }), "#app");

        visible.Value = true;
        items.Value = new List<int> { 1 };

        Assert.Equal("<div><b>dark</b><!----><ul><li>dark1</li><!----></ul></div>", Dom.Html);
    }

    [Fact]
    public void Store_shared_through_context_updates_all_consumers()
    {
        var store = new Store();
        App.Mount(new Lambda(() =>
        {
            Provide(StoreContext, store);
            return Div(
                new Lambda(() => { var s = Use(StoreContext); return Span(Text(s.Count)); }),
                new Lambda(() => { var s = Use(StoreContext); return Button(OnClick(() => s.Count.Value++)); }));
        }), "#app");

        Fire(Dom.Query("button")!, "click");

        Assert.Equal("<div><span>1</span><button></button></div>", Dom.Html);
    }

    [Fact]
    public void Store_state_survives_route_changes()
    {
        Router.Install(new MemoryHistory("/a"));
        try
        {
            var store = new Store();
            App.Mount(new Lambda(() =>
            {
                Provide(StoreContext, store);
                return Div(Routes(
                    Route("/a", () => new Lambda(() =>
                    {
                        var s = Use(StoreContext);
                        return Button(OnClick(() => s.Count.Value++), Text(s.Count));
                    }).Render()),
                    Route("/b", () => P("b"))));
            }), "#app");

            Fire(Dom.Query("button")!, "click");
            Fire(Dom.Query("button")!, "click");
            Router.Current!.Navigate("/b");
            Assert.Contains("<p>b</p>", Dom.Html);

            Router.Current!.Navigate("/a");

            Assert.Contains("<button>2</button>", Dom.Html);
        }
        finally
        {
            Router.Reset();
        }
    }

    private static Element B(params Content[] content) => El("b", content);
}

public class LifecycleTests : RendererTest
{
    [Fact]
    public void OnCleanup_runs_in_reverse_order_when_branch_is_removed()
    {
        var log = new List<string>();
        var visible = new Signal<bool>(true);
        App.Mount(new Lambda(() => Div(When(() => visible.Value, () => new Lambda(() =>
        {
            OnCleanup(() => log.Add("first"));
            OnCleanup(() => log.Add("second"));
            return P("x");
        }).Render()))), "#app");

        Assert.Empty(log);

        visible.Value = false;

        Assert.Equal(new[] { "second", "first" }, log);
    }

    [Fact]
    public void OnCleanup_inside_effect_runs_before_rerun_and_on_dispose()
    {
        var log = new List<string>();
        var n = new Signal<int>(1);
        var scope = new Scope();

        scope.Run(() =>
        {
            _ = new Effect(() =>
            {
                var v = n.Value;
                log.Add($"run {v}");
                OnCleanup(() => log.Add($"cleanup {v}"));
            });
        });

        n.Value = 2;
        scope.Dispose();

        Assert.Equal(new[] { "run 1", "cleanup 1", "run 2", "cleanup 2" }, log);
    }

    [Fact]
    public void OnCleanup_outside_any_owner_throws()
    {
        Assert.Throws<InvalidOperationException>(() => OnCleanup(() => { }));
    }

    [Fact]
    public void OnMount_runs_once_after_nodes_reach_the_host()
    {
        var calls = 0;
        var batchesAtMount = -1;
        var attached = false;
        App.Mount(new Lambda(() =>
        {
            var p = P("hello");
            OnMount(() =>
            {
                calls++;
                batchesAtMount = Dom.BatchCount;
                attached = Dom.Nodes.TryGetValue(p.Id, out var n) && n.Parent is not null;
            });
            return Div(p);
        }), "#app");

        Assert.Equal(1, calls);
        Assert.Equal(1, batchesAtMount);
        Assert.True(attached);
    }

    [Fact]
    public void OnMount_writes_are_flushed_in_a_follow_up_batch()
    {
        var text = new Signal<string>("before");
        App.Mount(new Lambda(() =>
        {
            OnMount(() => text.Value = "after");
            return P(Text(text));
        }), "#app");

        Assert.Equal("<p>after</p>", Dom.Html);
        Assert.Equal(2, Dom.BatchCount);
    }

    [Fact]
    public void OnMount_can_focus_an_element()
    {
        App.Mount(new Lambda(() =>
        {
            var input = Input();
            OnMount(input.Focus);
            return Div(input);
        }), "#app");

        Assert.Contains(Dom.Log, l => l.Op == "Focus");
    }

    [Fact]
    public void OnMount_is_skipped_when_scope_disposed_before_flush()
    {
        var called = false;
        var visible = new Signal<bool>(true);
        App.Mount(new Lambda(() => Div(When(() => visible.Value, () => P("x")))), "#app");

        Scheduler.Batch(() =>
        {
            visible.Value = false;
            visible.Value = true;               // branch rebuilt...
            var scope = new Scope(null);
            scope.Run(() => OnMount(() => called = true));
            scope.Dispose();                    // ...but this one is gone before the flush
        });

        Assert.False(called);
    }

    [Fact]
    public void OnMount_in_routed_page_runs_on_each_navigation()
    {
        Router.Install(new MemoryHistory("/a"));
        try
        {
            var mounts = new List<string>();
            App.Mount(new Lambda(() => Div(Routes(
                Route("/a", () => { OnMount(() => mounts.Add("a")); return P("a"); }),
                Route("/b", () => { OnMount(() => mounts.Add("b")); return P("b"); })))), "#app");

            Router.Current!.Navigate("/b");
            Router.Current!.Navigate("/a");

            Assert.Equal(new[] { "a", "b", "a" }, mounts);
        }
        finally
        {
            Router.Reset();
        }
    }
}

internal sealed class Lambda : Component
{
    private readonly Func<Node> build;
    public Lambda(Func<Node> build) => this.build = build;
    protected override Node Build() => build();
}
