using NetWasm.UI.Dom;
using NetWasm.UI.Reactive;
using static NetWasm.UI.Html;

namespace NetWasm.UI.Tests;

public class ComponentTests : RendererTest
{
    private sealed class Counter : Component
    {
        public readonly Signal<int> Count = new(0);

        protected override Node Build() =>
            Div(Class("counter"),
                H1("Counter"),
                P("Count: ", Text(() => Count.Value)),
                Button("Increment", OnClick(() => Count.Value++)));
    }

    [Fact]
    public void Mount_renders_initial_tree_in_one_batch()
    {
        App.Mount(new Counter(), "#app");

        Assert.Equal(1, Dom.BatchCount);
        Assert.Equal("<div class=\"counter\"><h1>Counter</h1><p>Count: 0</p><button>Increment</button></div>", Dom.Html);
    }

    [Fact]
    public void Click_updates_only_the_bound_text_node_in_one_batch()
    {
        App.Mount(new Counter(), "#app");
        Dom.Log.Clear();

        Fire(Dom.Query("button")!, "click");

        Assert.Equal(2, Dom.BatchCount);
        Assert.Equal("<div class=\"counter\"><h1>Counter</h1><p>Count: 1</p><button>Increment</button></div>", Dom.Html);
        var single = Assert.Single(Dom.Log);
        Assert.Equal("SetText", single.Op);
    }

    [Fact]
    public void Multiple_signal_writes_in_one_handler_produce_one_batch()
    {
        var first = new Signal<string>("a");
        var last = new Signal<string>("b");
        var root = new Lambda(() =>
            Div(Span(Text(() => first.Value)), Span(Text(() => last.Value)),
                Button(OnClick(() => { first.Value = "X"; last.Value = "Y"; }))));
        App.Mount(root, "#app");
        var before = Dom.BatchCount;

        Fire(Dom.Query("button")!, "click");

        Assert.Equal(before + 1, Dom.BatchCount);
        Assert.Equal("<div><span>X</span><span>Y</span><button></button></div>", Dom.Html);
    }

    [Fact]
    public void Reactive_class_attribute_updates()
    {
        var active = new Signal<bool>(false);
        App.Mount(new Lambda(() => Div(Class(() => active.Value ? "on" : "off"))), "#app");
        Assert.Equal("<div class=\"off\"></div>", Dom.Html);

        active.Value = true;

        Assert.Equal("<div class=\"on\"></div>", Dom.Html);
    }

    [Fact]
    public void Toggle_attribute_is_removed_when_false()
    {
        var busy = new Signal<bool>(true);
        App.Mount(new Lambda(() => Button(Disabled(() => busy.Value), "Save")), "#app");
        Assert.Equal("<button disabled>Save</button>", Dom.Html);

        busy.Value = false;

        Assert.Equal("<button>Save</button>", Dom.Html);
    }

    [Fact]
    public void Show_builds_and_tears_down_branch()
    {
        var visible = new Signal<bool>(false);
        var inner = new Signal<string>("hello");
        App.Mount(new Lambda(() => Div(When(() => visible.Value, () => P(Text(() => inner.Value)), () => Span("hidden")))), "#app");
        Assert.Equal("<div><span>hidden</span><!----></div>", Dom.Html);
        var nodesBefore = Dom.Nodes.Count;

        visible.Value = true;
        Assert.Equal("<div><p>hello</p><!----></div>", Dom.Html);

        inner.Value = "world";
        Assert.Equal("<div><p>world</p><!----></div>", Dom.Html);

        visible.Value = false;
        Assert.Equal("<div><span>hidden</span><!----></div>", Dom.Html);
        // Handles of the removed branch were released; the fallback has the same node count as the original fallback.
        Assert.Equal(nodesBefore, Dom.Nodes.Count);

        // The disposed branch must not react any more.
        Dom.Log.Clear();
        inner.Value = "ignored";
        Assert.Empty(Dom.Log);
    }

    [Fact]
    public void Bind_two_way_updates_signal_and_property()
    {
        var name = new Signal<string>("");
        App.Mount(new Lambda(() => Div(Input(Spread(Bind(name))), P(Text(() => $"Hi {name.Value}")))), "#app");
        var input = Dom.Query("input")!;
        Assert.Equal("", input.Properties["value"]);

        Fire(input, "input", "Ada");

        Assert.Equal("Ada", name.Value);
        Assert.Equal("Ada", input.Properties["value"]);
        Assert.Equal("<div><input><p>Hi Ada</p></div>", Dom.Html);
    }

    [Fact]
    public void Disposing_component_removes_root_and_releases_handles()
    {
        var c = new Counter();
        App.Mount(c, "#app");
        Assert.NotEmpty(Dom.Nodes);

        Scheduler.Batch(c.Dispose);

        Assert.Equal("", Dom.Html);
        Assert.Empty(Dom.Nodes);
    }

    [Fact]
    public void Listener_flags_are_encoded()
    {
        App.Mount(new Lambda(() => Form(OnSubmit(_ => { }))), "#app");

        var form = Dom.Query("form")!;
        var listener = Assert.Single(form.Listeners);
        Assert.Equal("submit", listener.Value.Event);
        Assert.Equal((byte)ListenerFlags.PreventDefault, listener.Value.Flags);
    }

    /// <summary>Component built from a lambda, for terse tests.</summary>
    private sealed class Lambda : Component
    {
        private readonly Func<Node> build;
        public Lambda(Func<Node> build) => this.build = build;
        protected override Node Build() => build();
    }
}
