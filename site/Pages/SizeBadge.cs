using NetWasm.UI;
using NetWasm.UI.Reactive;
using static NetWasm.UI.Html;

namespace Site.Pages;

/// <summary>Measures what this page actually downloaded, using the Resource Timing API through [JSImport].</summary>
public sealed class SizeBadge : Component
{
    protected override Node Build()
    {
        var page = new Signal<int>(0);
        var wasm = new Signal<int>(0);
        OnMount(() =>
        {
            page.Value = SiteJs.PageBytes();
            wasm.Value = SiteJs.WasmBytes();
        });

        return Div(
            Class("size-badge"),
            When(
                () => page.Value > 0,
                () => P(
                    "This page is ",
                    Strong(Text(() => Kb(page.Value))),
                    " over the wire, ",
                    Strong(Text(() => Kb(wasm.Value))),
                    " of it the Wasm of this very site. Measured live by your browser."),
                () => P("Measuring this page…")));
    }

    private static string Kb(int bytes)
    {
        var tenths = (bytes * 10 + 512) / 1024;
        return (tenths / 10).ToString() + "." + (tenths % 10).ToString() + " KB";
    }
}
