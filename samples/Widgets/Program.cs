using System.Runtime.InteropServices.JavaScript;
using NetWasm.UI;
using NetWasm.UI.Browser;
using NetWasm.UI.Reactive;
using static NetWasm.UI.Html;

namespace Widgets;

public static partial class Program
{
    public static void Main()
    {
    }

    /// <summary>Mounts a counter widget directly into the root the page gave this instance's DOM host.</summary>
    [JSExport("start")]
    public static void Start(string label) => BrowserApp.Start(new CounterWidget(label), "");

    [JSExport("stop")]
    public static void Stop() => BrowserApp.Stop();
}

public sealed class CounterWidget(string label) : Component
{
    private readonly Signal<int> count = new(0);

    protected override Node Build() =>
        Div(Class("widget"),
            Span(Class("label"), label),
            Span(Class("count"), Text(() => count.Value.ToString())),
            Button(Class("inc"), "+1", OnClick(() => count.Value++)));
}
