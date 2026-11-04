using NetWasm.UI.Dom;
using NetWasm.UI.Reactive;
using NetWasm.UI.Tests.Templates;
using static NetWasm.UI.Html;

namespace NetWasm.UI.Tests;

public class ServicesTests : RendererTest
{
    private static Element B(params Content[] content) => El("b", content);

    private interface IApi
    {
        string Name { get; }
    }

    private sealed class Api : IApi
    {
        public Api(string name) => Name = name;

        public string Name { get; }
    }

    private sealed class Tracked : IDisposable
    {
        public int Disposals { get; private set; }

        public void Dispose() => Disposals++;
    }

    private sealed class Greetings : IGreetings
    {
        public string Hello => "Hej";

        public Signal<int> Count { get; } = new(1);
    }

    [Fact]
    public void App_wide_services_are_injected_anywhere_in_the_tree()
    {
        var services = new Services().Add<IApi>(new Api("root"));

        App.Mount(new Lambda(() => Div(new Lambda(() => Span(Inject<IApi>().Name)))), "#app", services);

        Assert.Equal("<div><span>root</span></div>", Dom.Html);
    }

    [Fact]
    public void Services_are_keyed_by_the_registered_type()
    {
        var services = new Services().Add(new Api("concrete"));

        App.Mount(new Lambda(() => P(
            Inject<Api>().Name,
            TryInject<IApi>(out _) ? " yes" : " no")), "#app", services);

        Assert.Equal("<p>concrete no</p>", Dom.Html);
    }

    [Fact]
    public void Missing_service_throws_a_helpful_error()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => App.Mount(new Lambda(() => P(Inject<IApi>().Name)), "#app"));

        Assert.Contains("Services.Add", ex.Message);
    }

    [Fact]
    public void Inject_outside_a_build_throws_and_TryInject_returns_false()
    {
        Assert.Throws<InvalidOperationException>(() => Inject<IApi>());
        Assert.False(TryInject<IApi>(out var api));
        Assert.Null(api);
    }

    [Fact]
    public void Provide_overrides_for_descendants_only()
    {
        var services = new Services().Add<IApi>(new Api("root"));

        App.Mount(new Lambda(() => Div(
            new Lambda(() =>
            {
                Provide(new Services().Add<IApi>(new Api("inner")));
                return Div(new Lambda(() => Span(Inject<IApi>().Name)));
            }),
            Provide(new Services().Add<IApi>(new Api("scoped")), () => B(Inject<IApi>().Name)),
            new Lambda(() => Span(Inject<IApi>().Name)))), "#app", services);

        Assert.Equal("<div><div><span>inner</span></div><b>scoped</b><span>root</span></div>", Dom.Html);
    }

    [Fact]
    public void Services_are_visible_in_lazily_built_branches()
    {
        var visible = new Signal<bool>(false);
        var items = new Signal<IReadOnlyList<int>>(new List<int>());
        var services = new Services().Add<IApi>(new Api("x"));

        App.Mount(new Lambda(() => Div(
            When(() => visible.Value, () => B(Inject<IApi>().Name)),
            Ul(Each(items, i => i, i => Li(Inject<IApi>().Name + i))))), "#app", services);

        visible.Value = true;
        items.Value = new List<int> { 1 };

        Assert.Equal("<div><b>x</b><!----><ul><li>x1</li><!----></ul></div>", Dom.Html);
    }

    [Fact]
    public void Lazy_services_are_created_once_per_providing_scope_and_disposed_with_it()
    {
        var created = new List<Tracked>();
        var services = new Services().AddLazy(() =>
        {
            var t = new Tracked();
            created.Add(t);
            return t;
        });

        App.Mount(new Lambda(() =>
        {
            var a = Inject<Tracked>();
            var b = Inject<Tracked>();
            return P(ReferenceEquals(a, b) ? "same" : "different");
        }), "#app", services);

        Assert.Equal("<p>same</p>", Dom.Html);
        Assert.Single(created);
        Assert.Equal(0, created[0].Disposals);

        App.Unmount();

        Assert.Equal(1, created[0].Disposals);
    }

    [Fact]
    public void Lazy_services_that_are_never_injected_are_never_created()
    {
        var calls = 0;
        var services = new Services().AddLazy(() =>
        {
            calls++;
            return new Tracked();
        });

        App.Mount(new Lambda(() => P("x")), "#app", services);
        App.Unmount();

        Assert.Equal(0, calls);
    }

    [Fact]
    public void Each_provider_scope_gets_its_own_lazy_instance()
    {
        var shared = new Services().AddLazy(() => new Tracked());
        Tracked? inBranch = null;
        Tracked? outside = null;
        var show = new Signal<bool>(true);

        App.Mount(new Lambda(() => Div(
            When(() => show.Value, () => Provide(shared, () =>
            {
                inBranch = Inject<Tracked>();
                return B("a");
            })),
            Provide(shared, () =>
            {
                outside = Inject<Tracked>();
                return B("b");
            }))), "#app");

        Assert.NotNull(inBranch);
        Assert.NotSame(inBranch, outside);

        show.Value = false;

        Assert.Equal(1, inBranch!.Disposals);
        Assert.Equal(0, outside!.Disposals);
    }
    [Fact]
    public void Lazy_factories_can_inject_other_services_and_cycles_are_reported()
    {
        var services = new Services()
            .Add("prefix")
            .AddLazy<IApi>(() => new Api(Inject<string>() + "-api"));

        App.Mount(new Lambda(() => P(Inject<IApi>().Name)), "#app", services);
        Assert.Equal("<p>prefix-api</p>", Dom.Html);

        App.Unmount();
        Renderer.Reset();
        Renderer.Initialize(Dom);

        var cyclic = new Services().AddLazy<IApi>(() => Inject<IApi>());
        var ex = Assert.Throws<InvalidOperationException>(() => App.Mount(new Lambda(() => P(Inject<IApi>().Name)), "#app", cyclic));
        Assert.Contains("Circular", ex.Message);
    }

    [Fact]
    public void Lazy_factories_resolve_from_the_providing_scope_not_the_consumer()
    {
        var services = new Services()
            .Add("root")
            .AddLazy<IApi>(() => new Api(Inject<string>()));

        App.Mount(new Lambda(() =>
            Provide(new Services().Add("inner"), () => P(Inject<IApi>().Name))), "#app", services);

        Assert.Equal("<p>root</p>", Dom.Html);
    }

    [Fact]
    public void Instances_added_by_the_caller_are_not_disposed()
    {
        var tracked = new Tracked();

        App.Mount(new Lambda(() => P(Inject<Tracked>().Disposals.ToString())), "#app", new Services().Add(tracked));
        App.Unmount();

        Assert.Equal(0, tracked.Disposals);
    }

    [Fact]
    public void Null_registrations_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => new Services().Add<IApi>(null!));
        Assert.Throws<ArgumentNullException>(() => new Services().AddLazy<IApi>(null!));
        Assert.Throws<ArgumentNullException>(() => App.Mount(new Lambda(() => P("x")), "#app", null!));
    }

    [Fact]
    public void Inject_directive_fills_the_field_before_OnInit()
    {
        var greetings = new Greetings();

        App.Mount(new Greeter { Name = "Ada" }, "#app", new Services().Add<IGreetings>(greetings));
        Assert.Equal("<p class=\"greeter\">Hej, Ada! (1)</p>", Dom.Html);

        greetings.Count.Value = 2;
        Assert.Equal("<p class=\"greeter\">Hej, Ada! (2)</p>", Dom.Html);
    }

    [Fact]
    public void Component_tags_inject_constructor_parameters()
    {
        var greetings = new Greetings();

        App.Mount(new WelcomeHost(), "#app", new Services().Add<IGreetings>(greetings));
        Assert.Equal("<div><span>Hej Ada (1)</span><em>Hej</em></div>", Dom.Html);

        greetings.Count.Value = 3;
        Assert.Equal("<div><span>Hej Ada (3)</span><em>Hej</em></div>", Dom.Html);
    }

    [Fact]
    public void Constructor_injection_without_a_provider_throws()
    {
        Assert.Throws<InvalidOperationException>(() => App.Mount(new WelcomeHost(), "#app"));
    }
}
