using System.Runtime.InteropServices.JavaScript;
using NetWasm.UI.Browser;

namespace NetWasmBench;

public static partial class Program
{
    public static void Main()
    {
    }

    [JSExport("start")]
    public static void Start()
    {
        BrowserApp.Start(new BenchApp(), "#main");
    }

    /// <summary>Runs one of the DOM-free workloads in <see cref="ReactiveBench"/> and returns its checksum.</summary>
    [JSExport("reactive")]
    public static int Reactive(string name) => ReactiveBench.Run(name);
}
