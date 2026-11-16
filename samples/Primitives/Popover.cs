using NetWasm.UI;
using NetWasm.UI.Reactive;
using static NetWasm.UI.Html;

namespace Primitives;

/// <summary>
/// A trigger plus a floating panel rendered through a portal at the end of <c>body</c>. It closes on a
/// pointer down outside the panel (a document listener with <c>within</c>) and on Escape, which also
/// returns focus to the trigger. The position is read from the trigger's rect when it opens.
/// </summary>
public sealed class Popover : Component
{
    public string Label { get; set; } = "Open";

    public Signal<bool> Open { get; set; } = new(false);

    public Slot? ChildContent { get; set; }

    protected override Node Build()
    {
        var panelId = UseId("popover");
        var position = new Signal<string>("");
        Element trigger = null!;
        trigger = Button(
            Type("button"),
            Attr("aria-haspopup", "dialog"),
            Attr("aria-controls", panelId),
            Attr("aria-expanded", () => Open.Value ? "true" : "false"),
            Splat(Attributes),
            OnClick(() =>
            {
                if (!Open.Value)
                {
                    var r = trigger.GetRect();
                    position.Value = "top:" + (r.Bottom + 4).ToString() + "px;left:" + r.Left.ToString() + "px";
                }

                Open.Value = !Open.Value;
            }),
            Text(Label));

        var panel = Div(
            Id(panelId),
            Class("popover"),
            Attr("role", "dialog"),
            Attr("hidden", () => Open.Value ? null : ""),
            Style(() => position.Value),
            ChildContent is null ? Spread() : ChildContent);

        new Effect(() =>
        {
            if (!Open.Value)
            {
                return;
            }

            // Owned by this effect run: removed when Open turns false or the component goes away.
            OnDocument("pointerdown", e =>
            {
                if (!e.Inside && !e.IsTarget(trigger))
                {
                    Open.Value = false;
                }
            }, within: panel);
            OnDocument("keydown", e =>
            {
                if (e.Key == "Escape")
                {
                    Open.Value = false;
                    trigger.Focus();
                }
            });
        });

        return Span(Class("popover-root"), trigger, Html.Portal("body", panel));
    }
}
