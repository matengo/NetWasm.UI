using NetWasm.UI.Dom;
using NetWasm.UI.Forms;
using NetWasm.UI.Reactive;
using static NetWasm.UI.Html;

namespace NetWasm.UI.Tests;

public class PrimitivesTests : RendererTest
{
    [Fact]
    public void Element_methods_are_emitted_in_order_with_their_arguments()
    {
        Element? dialog = null;
        App.Mount(new Lambda(() => dialog = new Element("dialog")), "#app");
        Dom.Log.Clear();

        dialog!.ShowModal();
        dialog.Close("ok");
        dialog.ScrollIntoView("center");
        dialog.Call("setAttribute", "a", "b");
        Renderer.Flush();

        Assert.Equal(
            new[] { $"{dialog.Id} showModal()", $"{dialog.Id} close(ok)", $"{dialog.Id} scrollIntoView(center)", $"{dialog.Id} setAttribute(a, b)" },
            Dom.Log.Where(l => l.Op == "Call").Select(l => l.Detail));
    }

    [Fact]
    public void Document_listener_lives_as_long_as_its_scope()
    {
        var show = new Signal<bool>(true);
        var keys = new List<string>();
        App.Mount(new Lambda(() => Div(When(() => show.Value, () =>
        {
            OnDocument("keydown", e => keys.Add(e.Key));
            return Span();
        }))), "#app");

        var (handlerId, listener) = Assert.Single(Dom.GlobalListeners);
        Assert.Equal((GlobalTarget.Document, "keydown"), (listener.Target, listener.Event));

        Renderer.Dispatch(handlerId, new DomEvent("keydown", key: "Escape").Encode());
        Assert.Equal(new[] { "Escape" }, keys);

        show.Value = false;
        Assert.Empty(Dom.GlobalListeners);
        Assert.Contains(Dom.Log, l => l.Op == "RemoveGlobalListener" && l.Detail == $"#{handlerId}");
    }

    [Fact]
    public void Window_listener_from_an_event_handler_is_disposed_by_the_caller()
    {
        IDisposable? sub = null;
        Element? panel = null;
        App.Mount(new Lambda(() =>
        {
            panel = Div();
            return Button(OnClick(() => sub = OnWindow("pointerdown", _ => { }, within: panel)), panel);
        }), "#app");

        Fire(Dom.Query("button")!, "click");
        var listener = Assert.Single(Dom.GlobalListeners).Value;
        Assert.Equal(GlobalTarget.Window, listener.Target);
        Assert.Equal(panel!.Id, listener.WithinId);

        sub!.Dispose();
        sub.Dispose();
        Renderer.Flush();
        Assert.Empty(Dom.GlobalListeners);
        Assert.Single(Dom.Log, l => l.Op == "RemoveGlobalListener");
    }

    [Fact]
    public void Extended_event_fields_round_trip()
    {
        var e = new DomEvent("focusout", "v") { TargetId = 7, RelatedTargetId = 9, NewState = "open", Inside = true, RelatedInside = true };

        var d = DomEvent.Decode("focusout", e.Encode());

        Assert.Equal(7, d.TargetId);
        Assert.Equal(9, d.RelatedTargetId);
        Assert.Equal("open", d.NewState);
        Assert.True(d.Inside);
        Assert.True(d.RelatedInside);
        Assert.Equal("v", d.Value);
    }

    [Fact]
    public void Short_payloads_decode_with_empty_extended_fields()
    {
        var d = DomEvent.Decode("click", new DomEvent("click", key: "a").Encode());

        Assert.Equal(0, d.TargetId);
        Assert.Equal("", d.NewState);
        Assert.False(d.Inside);
        Assert.Equal("", default(DomEvent).NewState);
    }

    [Fact]
    public void IsTarget_compares_the_target_id()
    {
        var div = new Element("div");
        Assert.True(new DomEvent("click") { TargetId = div.Id }.IsTarget(div));
        Assert.False(new DomEvent("click").IsTarget(div));
    }

    [Fact]
    public void Reads_are_refused_while_building()
    {
        InvalidOperationException? error = null;
        App.Mount(new Lambda(() =>
        {
            var div = Div();
            error = Assert.Throws<InvalidOperationException>(() => div.GetRect());
            return div;
        }), "#app");

        Assert.NotNull(error);
        Assert.Equal(0, Dom.Reads);
    }

    [Fact]
    public void Reads_flush_pending_ops_and_answer_from_OnMount()
    {
        Rect rect = default;
        bool contains = false, focused = false;
        App.Mount(new Lambda(() =>
        {
            var inner = Span();
            var outer = Div(inner);
            OnMount(() =>
            {
                Dom.Rects[outer.Id] = new Rect(1, 2, 30, 40);
                Dom.ActiveId = inner.Id;
                rect = outer.GetRect();
                contains = outer.Contains(inner);
                focused = inner.HasFocus();
            });
            return outer;
        }), "#app");

        Assert.Equal(new Rect(1, 2, 30, 40), rect);
        Assert.Equal(31, rect.Right);
        Assert.True(contains);
        Assert.True(focused);
        Assert.Equal(1024, Viewport().Width);
    }

    [Fact]
    public void Reads_need_a_host_that_supports_them()
    {
        Renderer.Reset();
        Renderer.Initialize(new WriteOnlyHost());

        Assert.Throws<NotSupportedException>(() => ActiveElementId());
    }

    [Fact]
    public void OnResize_reports_the_new_rect_and_stops_with_its_scope()
    {
        var show = new Signal<bool>(true);
        var sizes = new List<double>();
        App.Mount(new Lambda(() => Div(When(() => show.Value, () =>
        {
            var box = Div();
            OnResize(box, r => sizes.Add(r.Width));
            return box;
        }))), "#app");

        var (handlerId, nodeId) = Assert.Single(Dom.ResizeObservers);
        Dom.Rects[nodeId] = new Rect(0, 0, 120, 10);
        Renderer.Dispatch(handlerId, []);
        Assert.Equal(new[] { 120.0 }, sizes);

        show.Value = false;
        Assert.Empty(Dom.ResizeObservers);
    }

    [Fact]
    public void Portal_renders_into_its_target_and_is_removed_with_its_owner()
    {
        var show = new Signal<bool>(true);
        App.Mount(new Lambda(() => Div(When(() => show.Value, () => Span(Html.Portal("#overlays", P("toast")))))), "#app");

        var (hostId, selector) = Assert.Single(Dom.Portals);
        Assert.Equal("#overlays", selector);
        Assert.Equal("<div data-nwui-portal style=\"display:contents\"><p>toast</p></div>", Dom[hostId].ToHtml());
        Assert.Same(Dom.Document, Dom[hostId].Parent);

        show.Value = false;
        Assert.DoesNotContain(Dom.Document.Children, c => c.Tag == "div" && c.Attributes.ContainsKey("data-nwui-portal"));
        Assert.False(Dom.Nodes.ContainsKey(hostId));
    }

    [Fact]
    public void UseId_and_field_ids_share_one_counter()
    {
        var a = UseId();
        var field = new Field<int>(0);
        var b = UseId("menu");
        var other = new Field<string>("");

        Assert.StartsWith("nw", a);
        Assert.StartsWith("menu", b);
        Assert.NotEqual(field.Id, other.Id);
        Assert.Equal(4, new[] { a, field.Id, b, other.Id }.Select(s => s.TrimStart('n', 'w', 'f', 'm', 'e', 'u')).Distinct().Count());
    }

    [Fact]
    public void Slot_with_argument_builds_per_call()
    {
        var cell = new Slot<int>(n => Span(n.ToString()));
        App.Mount(new Lambda(() => Ul(Li(cell.With(1)), Li(TemplateRuntime.Render(cell, 2)), Li(TemplateRuntime.Render<int>(null, 3)))), "#app");

        Assert.Equal("<ul><li><span>1</span></li><li><span>2</span></li><li></li></ul>", Dom.Html);
    }

    [Fact]
    public void AttrStr_formats_booleans_in_lowercase()
    {
        Assert.Equal("true", TemplateRuntime.AttrStr(true));
        Assert.Equal("false", TemplateRuntime.AttrStr((bool?)false));
        Assert.Null(TemplateRuntime.AttrStr((bool?)null));
        Assert.Equal("True", TemplateRuntime.Str(true));
        Assert.Equal("3", TemplateRuntime.AttrStr(3));
    }

    private sealed class WriteOnlyHost : IDomHost
    {
        public void ApplyBatch(ReadOnlySpan<byte> ops)
        {
        }
    }

    private sealed class Lambda : Component
    {
        private readonly Func<Node> build;
        public Lambda(Func<Node> build) => this.build = build;
        protected override Node Build() => build();
    }
}
