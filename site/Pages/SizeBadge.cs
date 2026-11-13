using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NetWasm.UI;
using NetWasm.UI.Reactive;
using static NetWasm.UI.Html;

namespace Site.Pages;

/// <summary>
/// Measures what this page actually downloaded, using the Resource Timing API through [JSImport],
/// and types the result out word by word.
/// </summary>
/// <remarks>
/// Every word is always laid out, and hidden until it is typed, so the badge has its final size
/// from the first (prerendered) paint and the page does not shift.
/// </remarks>
public sealed class SizeBadge : Component
{
    private const string Before = "This page is ";
    private const string Middle = " over the wire, ";
    private const string After = " of it the Wasm of this very site. Measured live by your browser.";

    protected override Node Build()
    {
        var page = new Signal<int>(0);
        var wasm = new Signal<int>(0);
        var shown = new Signal<int>(0);

        var words = new List<Content>();
        void Plain(string text)
        {
            foreach (var word in text.Split(' '))
            {
                if (word.Length > 0) words.Add(Word(words.Count, shown, word + " "));
            }
        }

        Plain(Before);
        words.Add(Word(words.Count, shown, Strong(Text(() => Kb(page.Value))), " "));
        Plain(Middle);
        words.Add(Word(words.Count, shown, Strong(Text(() => Kb(wasm.Value))), " "));
        Plain(After);
        var count = words.Count;

        OnMount(() =>
        {
            page.Value = SiteJs.PageBytes();
            wasm.Value = SiteJs.WasmBytes();
            if (page.Value == 0) return;
            if (SiteJs.ReducedMotion()) shown.Value = count;
            else _ = Type(shown, count);
        });

        words.Insert(0, Span(
            Class(() => shown.Value == 0 ? "size-badge-wait" : "size-badge-wait done"),
            "Measuring this page…"));

        return Div(Class("size-badge"), P(words.ToArray()));
    }

    private static Element Word(int index, Signal<int> shown, params Content[] content) =>
        Span(
            Class(() => shown.Value <= index ? "w" : shown.Value == index + 1 ? "w on tip" : "w on"),
            Spread(content));

    // Uneven delays, like tokens arriving from a model.
    private static async Task Type(Signal<int> shown, int count)
    {
        for (var i = 1; i <= count; i++)
        {
            await Task.Delay(35 + (i * 37 % 5) * 12);
            var next = i;
            Scheduler.Batch(() => shown.Value = next);
        }
        await Task.Delay(600);
        Scheduler.Batch(() => shown.Value = count + 1);
    }

    private static string Kb(int bytes)
    {
        if (bytes == 0) return "000.0 KB";
        var tenths = (bytes * 10 + 512) / 1024;
        return (tenths / 10).ToString() + "." + (tenths % 10).ToString() + " KB";
    }
}
