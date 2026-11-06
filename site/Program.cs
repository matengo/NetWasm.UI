using System.Runtime.InteropServices.JavaScript;
using NetWasm.UI.Browser;

namespace Site;

public static partial class Program
{
    public static void Main()
    {
    }

    [JSExport("start")]
    public static void Start()
    {
        // The router reads <base href> from index.html ("/" locally, "/NetWasm.UI/" on GitHub Pages).
        BrowserApp.Start(new AppRoot(), "#app");
    }
}
