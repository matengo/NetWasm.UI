using System;
using NetWasm.UI.Dom;

namespace NetWasm.UI;

/// <summary>
/// Renders its <see cref="ChildContent"/> at the end of the element matching <see cref="Target"/> (<c>body</c> by
/// default) instead of where the component is placed, inside a <c>&lt;div data-nwui-portal style="display:contents"&gt;</c>.
/// Use it for toasts and overlays that must escape <c>overflow: hidden</c> or a stacking context; a modal
/// <c>&lt;dialog&gt;</c> or a <c>popover</c> already renders in the top layer and does not need one.
/// The content still belongs to the component's scope (context, cleanup, error boundaries) and is removed with it.
/// Not rendered during prerendering.
/// </summary>
public sealed class Portal : Component
{
    /// <summary>A CSS selector for the element to append to. Default: <c>body</c>.</summary>
    public string Target { get; set; } = "body";

    public Slot? ChildContent { get; set; }

    protected override Node Build()
    {
        if (string.IsNullOrEmpty(Target))
        {
            throw new InvalidOperationException("Portal needs a Target selector.");
        }

        var host = new Element("div", Html.Attr("data-nwui-portal", ""), Html.Attr("style", "display:contents"), Html.Splat(Attributes, baseStyle: "display:contents"));
        if (ChildContent is not null)
        {
            ((IContent)ChildContent).ApplyTo(host);
        }

        Renderer.MountPortal(host.Id, Target);
        Html.OnCleanup(host.Remove);
        return new Anchor();
    }
}
