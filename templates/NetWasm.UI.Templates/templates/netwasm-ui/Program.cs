using System.Runtime.InteropServices.JavaScript;
using NetWasm.UI.Browser;

namespace NetWasmApp;

public static partial class Program
{
    public static void Main()
    {
    }

    /// <summary>Called from wwwroot/app.mjs once the module is loaded.</summary>
    [JSExport("start")]
    public static void Start() => BrowserApp.Start(new App(), "#app");
}
