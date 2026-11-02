using NetWasm.UI.Reactive;
using static NetWasm.UI.Html;

namespace NetWasm.UI.Tests;

public class ErrorBoundaryTests : RendererTest
{
    private static Node Fallback(BoundaryError e) => P("Error: " + e.Message);

    private static Node Boom(string message) => throw new InvalidOperationException(message);

    [Fact]
    public void Error_while_building_content_renders_fallback()
    {
        App.Mount(new Lambda(() => Div(ErrorBoundary(() => Boom("build"), Fallback))), "#app");

        Assert.Equal("<div><p>Error: build</p><!----></div>", Dom.Html);
    }

    [Fact]
    public void Content_without_error_renders_normally()
    {
        App.Mount(new Lambda(() => Div(ErrorBoundary(() => Span("ok"), Fallback))), "#app");

        Assert.Equal("<div><span>ok</span><!----></div>", Dom.Html);
    }

    [Fact]
    public void Error_from_nested_component_build_is_caught()
    {
        App.Mount(new Lambda(() => Div(ErrorBoundary(
            () => Section(new Lambda(() => Boom("child"))),
            Fallback))), "#app");

        Assert.Equal("<div><p>Error: child</p><!----></div>", Dom.Html);
    }

    [Fact]
    public void Error_in_effect_rerun_replaces_content_with_fallback()
    {
        var count = new Signal<int>(0);
        App.Mount(new Lambda(() => Div(ErrorBoundary(
            () => Span(Text(() => count.Value < 2 ? count.Value.ToString() : throw new InvalidOperationException("too big"))),
            Fallback))), "#app");

        count.Value = 1;
        Assert.Equal("<div><span>1</span><!----></div>", Dom.Html);

        count.Value = 2;
        Assert.Equal("<div><p>Error: too big</p><!----></div>", Dom.Html);
    }

    [Fact]
    public void Error_in_event_handler_goes_to_nearest_boundary_and_flushes_dom()
    {
        var label = new Signal<string>("a");
        App.Mount(new Lambda(() => Div(
            Span(Text(label)),
            ErrorBoundary(
                () => Button("go", OnClick(_ =>
                {
                    label.Value = "b";
                    throw new InvalidOperationException("click");
                })),
                Fallback))), "#app");

        Fire(Dom.Query("button")!, "click");

        Assert.Equal("<div><span>b</span><p>Error: click</p><!----></div>", Dom.Html);
    }

    [Fact]
    public void Error_in_on_mount_goes_to_boundary()
    {
        App.Mount(new Lambda(() => Div(ErrorBoundary(
            () => new Lambda(() =>
            {
                OnMount(() => throw new InvalidOperationException("mount"));
                return Span("content");
            }).Render(),
            Fallback))), "#app");

        Assert.Equal("<div><p>Error: mount</p><!----></div>", Dom.Html);
    }

    [Fact]
    public void Reset_rebuilds_content()
    {
        var fail = true;
        App.Mount(new Lambda(() => Div(ErrorBoundary(
            () => fail ? Boom("first") : Span("recovered"),
            e => Button("retry", OnClick(_ => e.Reset()))))), "#app");

        Assert.Equal("<div><button>retry</button><!----></div>", Dom.Html);

        fail = false;
        Fire(Dom.Query("button")!, "click");

        Assert.Equal("<div><span>recovered</span><!----></div>", Dom.Html);
    }

    [Fact]
    public void Content_effects_are_disposed_when_fallback_is_shown()
    {
        var count = new Signal<int>(0);
        var runs = 0;
        App.Mount(new Lambda(() => Div(ErrorBoundary(
            () =>
            {
                _ = new Effect(() => { _ = count.Value; runs++; });
                return Button("x", OnClick(_ => throw new InvalidOperationException("x")));
            },
            Fallback))), "#app");

        Fire(Dom.Query("button")!, "click");
        var before = runs;
        count.Value = 5;

        Assert.Equal(before, runs);
    }

    [Fact]
    public void Error_in_fallback_goes_to_outer_boundary()
    {
        App.Mount(new Lambda(() => Div(ErrorBoundary(
            () => Section(ErrorBoundary(() => Boom("inner"), _ => Boom("fallback"))),
            Fallback))), "#app");

        Assert.Equal("<div><p>Error: fallback</p><!----></div>", Dom.Html);
    }

    [Fact]
    public void Nested_boundary_catches_before_outer()
    {
        App.Mount(new Lambda(() => Div(ErrorBoundary(
            () => Section(ErrorBoundary(() => Boom("inner"), e => El("b", e.Message))),
            Fallback))), "#app");

        Assert.Equal("<div><section><b>inner</b><!----></section><!----></div>", Dom.Html);
    }

    [Fact]
    public void Unhandled_event_error_goes_to_global_hook()
    {
        Exception? seen = null;
        App.OnError = ex => seen = ex;
        App.Mount(new Lambda(() => Button("x", OnClick(_ => throw new InvalidOperationException("global")))), "#app");

        Fire(Dom.Query("button")!, "click");

        Assert.Equal("global", seen?.Message);
    }

    [Fact]
    public void Unhandled_effect_error_goes_to_global_hook_and_dom_still_flushes()
    {
        var errors = new List<string>();
        App.OnError = ex => errors.Add(ex.Message);
        var count = new Signal<int>(0);
        App.Mount(new Lambda(() => Div(
            Span(Text(() => count.Value.ToString())),
            new Lambda(() =>
            {
                _ = new Effect(() =>
                {
                    if (count.Value == 1)
                    {
                        throw new InvalidOperationException("effect");
                    }
                });
                return El("b", "x");
            }))), "#app");

        count.Value = 1;

        Assert.Equal(new[] { "effect" }, errors);
        Assert.Equal("<div><span>1</span><b>x</b></div>", Dom.Html);
    }

    [Fact]
    public void Unhandled_error_without_hook_is_rethrown_after_the_dom_flush()
    {
        var label = new Signal<string>("a");
        App.Mount(new Lambda(() => Button(Text(label), OnClick(_ =>
        {
            label.Value = "b";
            throw new InvalidOperationException("rethrown");
        }))), "#app");

        var ex = Assert.Throws<InvalidOperationException>(() => Fire(Dom.Query("button")!, "click"));

        Assert.Equal("rethrown", ex.Message);
        Assert.Equal("<button>b</button>", Dom.Html);
    }

    [Fact]
    public void Error_in_cleanup_goes_to_global_hook()
    {
        string? seen = null;
        App.OnError = ex => seen = ex.Message;
        var show = new Signal<bool>(true);
        App.Mount(new Lambda(() => Div(When(() => show.Value, () => new Lambda(() =>
        {
            OnCleanup(() => throw new InvalidOperationException("cleanup"));
            return Span("x");
        }).Render()))), "#app");

        show.Value = false;

        Assert.Equal("cleanup", seen);
        Assert.Equal("<div><!----></div>", Dom.Html);
    }
}
