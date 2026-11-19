using System.Runtime.InteropServices.JavaScript;
using NetWasm.UI;
using NetWasm.UI.Browser;
using NetWasm.UI.Workers;
using Tsp.Contracts;

namespace Tsp;

public static partial class Program
{
    [JSExport("start")]
    public static void Start() =>
        BrowserApp.Start(
            new TspPage(),
            // navigator.hardwareConcurrency - 1 workers, each running one solver at a time.
            new Services().AddWorkerPool<ITspSolver>(),
            "#app");
}
