using System.Runtime.InteropServices.JavaScript;
using NetWasm.UI.Browser;

namespace Counter;

public static partial class Program
{
    public static void Main()
    {
    }

    [JSExport("start")]
    public static void Start()
    {
        BrowserApp.Start(new AppRoot(), "#app");
    }
}
