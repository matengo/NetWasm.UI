using System.Threading.Tasks;
using NetWasm.UI;
using NetWasm.UI.Reactive;
using static NetWasm.UI.Html;

namespace Site.Code;

/// <summary>A highlighted code block with a caption and a copy button.</summary>
public sealed class CodeBlock : Component
{
    public string Source { get; set; } = "";

    /// <summary><c>cs</c>, <c>nwui</c> or <c>sh</c>.</summary>
    public string Lang { get; set; } = "cs";

    public string Caption { get; set; } = "";

    protected override Node Build()
    {
        var copied = new Signal<bool>(false);
        var source = Source.Replace("\r\n", "\n").TrimEnd();

        return Div(
            Class("code"),
            Div(
                Class("code-bar"),
                Span(Class("code-caption"), Caption),
                Button(
                    Class("copy"),
                    Type("button"),
                    Attr("aria-label", "Copy code"),
                    OnClick(() =>
                    {
                        SiteJs.CopyText(source);
                        copied.Value = true;
                        _ = ResetLater(copied);
                    }),
                    Text(() => copied.Value ? "Copied" : "Copy"))),
            Pre(Html.Code(Class("lang-" + Lang), Spread(Highlighter.Highlight(source, Lang)))));
    }

    private static async Task ResetLater(Signal<bool> copied)
    {
        await Task.Delay(1500);
        // Not inside an event handler any more, so batch to flush the DOM update.
        Scheduler.Batch(() => copied.Value = false);
    }
}
