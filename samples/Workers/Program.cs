using System.Runtime.InteropServices.JavaScript;
using NetWasm.UI;
using NetWasm.UI.Browser;
using NetWasm.UI.Workers;
using Workers.Contracts;

namespace Workers;

public static partial class Program
{
    [JSExport("start")]
    public static void Start() =>
        BrowserApp.Start(
            new PrimesPage(),
            new Services()
                .AddWorker<IPrimeWorker>()          // one worker that keeps its state
                .AddWorkerPool<IPrimeMath>(size: 4), // four workers for parallel, stateless calls
            "#app");
}
