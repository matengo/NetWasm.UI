using System;
using NetWasm.UI;
using NetWasm.UI.Reactive;
using NetWasm.UI.Primitives;
using static NetWasm.UI.Html;

namespace Primitives.Theme;

/// <summary>
/// The design system's look for <see cref="Disclosure"/>: a bordered box with a title and a chevron. It only sets
/// classes and trigger markup; behaviour, ARIA and state come from the headless component, and theme.css styles them.
/// </summary>
public sealed class Expander : Component
{
    public string Title { get; set; } = "";

    public Signal<bool>? Open { get; set; }

    public bool DefaultOpen { get; set; }

    public Action<bool>? OnOpenChange { get; set; }

    public Prop<bool> Disabled { get; set; } = false;

    public Slot? ChildContent { get; set; }

    protected override Node Build() => new Disclosure
    {
        Open = Open,
        DefaultOpen = DefaultOpen,
        OnOpenChange = OnOpenChange,
        Disabled = Disabled,
        Trigger = new Slot(() => new Content[]
        {
            Span(Class("ui-expander-icon"), Attr("aria-hidden", "true")),
            Span(Class("ui-expander-title"), Text(Title)),
        }),
        ChildContent = ChildContent,
        // The theme's class first, then the caller's attributes: Splat joins the classes.
        Attributes = [Class("ui-expander"), .. Attributes],
    }.Render();
}
