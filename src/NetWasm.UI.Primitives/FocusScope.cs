using NetWasm.UI;
using static NetWasm.UI.Html;

namespace NetWasm.UI.Primitives;

/// <summary>
/// Manages focus for a panel that appears on top of the page (a menu, a non-modal dialog). Built only on core
/// primitives: <c>OnMount</c> + <see cref="Element.FocusFirst"/> to move focus in, <see cref="Html.ActiveElementId"/>
/// and <see cref="Html.Focus(int)"/> to give it back, and two sentinels plus a document <c>focusin</c> listener to
/// keep it inside. For a modal dialog prefer <c>&lt;dialog&gt;.showModal()</c>, which does all of this natively.
/// </summary>
public sealed class FocusScope : Component
{
    /// <summary>Moves focus to the first tabbable element (or the scope itself) when it mounts.</summary>
    public bool AutoFocus { get; set; } = true;

    /// <summary>Returns focus to the element that had it before, when the scope goes away.</summary>
    public bool RestoreFocus { get; set; } = true;

    /// <summary>Keeps Tab and Shift+Tab inside the scope, wrapping around at the ends.</summary>
    public bool Trap { get; set; }

    public Slot? ChildContent { get; set; }

    protected override Node Build()
    {
        var content = Div(
            Attr("data-focus-scope", ""),
            Attr("tabindex", "-1"),
            Splat(Attributes),
            ChildContent is null ? Spread() : ChildContent);

        var previous = 0;
        OnMount(() =>
        {
            if (RestoreFocus)
            {
                previous = ActiveElementId();
            }

            if (AutoFocus)
            {
                content.FocusFirst();
            }
        });
        if (RestoreFocus)
        {
            OnCleanup(() => Html.Focus(previous));
        }

        if (!Trap)
        {
            return content;
        }

        // Tabbing past either end lands on a sentinel, which sends focus round to the other end.
        var root = Div(
            Style("display:contents"),
            Sentinel(() => content.FocusLast()),
            content,
            Sentinel(() => content.FocusFirst()));
        OnDocument("focusin", e =>
        {
            if (!e.Inside)
            {
                content.FocusFirst();
            }
        }, within: root);
        return root;
    }

    private static Element Sentinel(System.Action onFocus) => Span(
        Attr("tabindex", "0"),
        Attr("aria-hidden", "true"),
        Attr("data-focus-sentinel", ""),
        Style("position:fixed;width:1px;height:1px;overflow:hidden;opacity:0;pointer-events:none"),
        On("focus", onFocus));
}
