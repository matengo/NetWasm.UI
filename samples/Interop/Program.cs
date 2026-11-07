using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;
using NetWasm.UI.Browser;

namespace Interop;

public static partial class Program
{
    public static void Main()
    {
    }

    /// <summary>An async export: the page awaits it, so the library is loaded before the first render.</summary>
    [JSExport("start")]
    public static async Task Start()
    {
        await MeterJs.Load();
        BrowserApp.Start(new InteropPage(), "#app");
    }
}