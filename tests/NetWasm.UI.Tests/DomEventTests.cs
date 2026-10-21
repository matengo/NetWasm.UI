using NetWasm.UI.Dom;
using static NetWasm.UI.Html;

namespace NetWasm.UI.Tests;

public class DomEventTests : RendererTest
{
    [Fact]
    public void Encode_decode_round_trips_all_fields()
    {
        var e = new DomEvent("keydown", value: "héllo", key: "Enter", isChecked: true,
            modifiers: EventModifiers.Ctrl | EventModifiers.Shift, button: 2, clientX: -5, clientY: 70000, repeat: true);

        var d = DomEvent.Decode("keydown", e.Encode());

        Assert.Equal("héllo", d.Value);
        Assert.Equal("Enter", d.Key);
        Assert.True(d.Checked);
        Assert.True(d.Ctrl);
        Assert.True(d.Shift);
        Assert.False(d.Alt);
        Assert.False(d.Meta);
        Assert.Equal(2, d.Button);
        Assert.Equal(-5, d.ClientX);
        Assert.Equal(70000, d.ClientY);
        Assert.True(d.Repeat);
        Assert.False(d.IsPlainClick);
    }

    [Fact]
    public void Wire_layout_matches_dom_mjs()
    {
        // u8 flags | i32 button | i32 x | i32 y | u32 len "a" | u32 len "bc"
        var bytes = new DomEvent("click", value: "bc", key: "a", modifiers: EventModifiers.Meta, button: 1, clientX: 3, clientY: 4).Encode();

        Assert.Equal(new byte[]
        {
            8,
            1, 0, 0, 0,
            3, 0, 0, 0,
            4, 0, 0, 0,
            1, 0, 0, 0, (byte)'a',
            2, 0, 0, 0, (byte)'b', (byte)'c',
        }, bytes);
    }

    [Fact]
    public void Empty_payload_is_a_plain_event()
    {
        var d = DomEvent.Decode("click", []);

        Assert.Equal("click", d.Type);
        Assert.True(d.IsPlainClick);
        Assert.Equal("", d.Value);
    }

    [Fact]
    public void Handler_receives_decoded_event_from_renderer()
    {
        DomEvent? seen = null;
        App.Mount(new Lambda(() => Button(OnClick(e => seen = e))), "#app");

        Fire(Dom.Query("button")!, new DomEvent("click", modifiers: EventModifiers.Ctrl, clientX: 10));

        Assert.NotNull(seen);
        Assert.True(seen.Value.Ctrl);
        Assert.Equal(10, seen.Value.ClientX);
        Assert.False(seen.Value.IsPlainClick);
    }

    [Fact]
    public void Plain_click_flag_is_encoded()
    {
        App.Mount(new Lambda(() => A(On("click", _ => { }, ListenerFlags.PreventDefaultPlainClick))), "#app");

        var listener = Assert.Single(Dom.Query("a")!.Listeners);
        Assert.Equal(4, listener.Value.Flags);
    }

    private sealed class Lambda : Component
    {
        private readonly Func<Node> build;
        public Lambda(Func<Node> build) => this.build = build;
        protected override Node Build() => build();
    }
}
