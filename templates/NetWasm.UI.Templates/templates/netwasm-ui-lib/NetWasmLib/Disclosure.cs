using System;
using NetWasm.UI;
using NetWasm.UI.Reactive;
using static NetWasm.UI.Html;

namespace NetWasmLib;

/// <summary>
/// A button that shows and hides a panel (the WAI-ARIA disclosure pattern). Headless: it renders the
/// structure, ARIA and <c>data-state</c>, and leaves the looks to the app or a design system.
/// </summary>
/// <remarks>
/// Uncontrolled, it keeps its own state, starting from <see cref="DefaultOpen"/>. Controlled, the caller
/// passes <see cref="Open"/> (in a template, <c>@bind-Open</c>) and owns the state. Either way
/// <see cref="OnOpenChange"/> reports a change the user made.
/// </remarks>
public sealed class Disclosure : Component
{
    /// <summary>The open state, owned by the caller. Leave it null to let the component keep its own.</summary>
    public Signal<bool>? Open { get; set; }

    /// <summary>The initial state when <see cref="Open"/> is not set.</summary>
    public bool DefaultOpen { get; set; }

    /// <summary>Called with the new state when the user toggles the panel.</summary>
    public Action<bool>? OnOpenChange { get; set; }

    /// <summary>Disables the trigger.</summary>
    public Prop<bool> Disabled { get; set; } = false;

    /// <summary>The content of the trigger button.</summary>
    public Slot? Trigger { get; set; }

    /// <summary>The content of the panel.</summary>
    public Slot? ChildContent { get; set; }

    protected override Node Build()
    {
        var open = Open ?? new Signal<bool>(DefaultOpen);
        var id = UseId("disclosure");
        string? State() => open.Value ? "open" : "closed";

        void Toggle()
        {
            var next = !open.Value;
            open.Value = next;
            OnOpenChange?.Invoke(next);
        }

        // The caller's attributes land on the root, so class, id, data-* and @on… handlers go there.
        return Div(
            Attr("data-state", State),
            Splat(Attributes),
            Button(
                Type("button"),
                Id(id + "-trigger"),
                Attr("aria-controls", id + "-panel"),
                Attr("aria-expanded", () => open.Value ? "true" : "false"),
                Attr("data-state", State),
                Attr("disabled", () => Disabled.Value ? "" : null),
                Attr("data-disabled", () => Disabled.Value ? "" : null),
                OnClick(Toggle),
                Trigger is null ? Spread() : Trigger),
            Div(
                Id(id + "-panel"),
                Attr("role", "region"),
                Attr("aria-labelledby", id + "-trigger"),
                Attr("data-state", State),
                Attr("hidden", () => open.Value ? null : ""),
                ChildContent is null ? Spread() : ChildContent));
    }
}
