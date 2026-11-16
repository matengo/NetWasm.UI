using NetWasm.UI;
using NetWasm.UI.Reactive;
using static NetWasm.UI.Html;

namespace Primitives;

/// <summary>
/// A modal on the native <c>&lt;dialog&gt;</c>: <see cref="Open"/> drives <c>showModal()</c>/<c>close()</c> through
/// element calls, and the dialog's own <c>close</c> event (Escape, form method=dialog) writes it back.
/// </summary>
public sealed class Dialog : Component
{
    public Signal<bool> Open { get; set; } = new(false);

    public string Title { get; set; } = "";

    public Slot? ChildContent { get; set; }

    protected override Node Build()
    {
        var titleId = UseId("dialog-title");
        var dialog = El("dialog",
            Attr("aria-labelledby", titleId),
            Splat(Attributes),
            On("close", () => Open.Value = false),
            H2(Id(titleId), Text(Title)),
            Div(ChildContent is null ? Spread() : ChildContent),
            Button(Class("dialog-close"), Type("button"), OnClick(() => Open.Value = false), Text("Close")));

        // showModal() needs the dialog in the document, so wait for the first flush.
        var mounted = new Signal<bool>(false);
        OnMount(() => mounted.Value = true);
        new Effect(() =>
        {
            if (!mounted.Value)
            {
                return;
            }

            if (Open.Value)
            {
                dialog.ShowModal();
            }
            else
            {
                dialog.Close();
            }
        });

        return dialog;
    }
}
