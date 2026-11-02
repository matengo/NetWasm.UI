using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using NetWasmBench;

namespace BlazorBench;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);
        builder.RootComponents.Add<App>("#main");
        await builder.Build().RunAsync();
    }

    /// <summary>The DOM-free workloads that do not need signals; the others return -1 (not applicable).</summary>
    [JSInvokable("Reactive")]
    public static int Reactive(string name) => name switch
    {
        "sort" => PlainBench.Sort(),
        "sieve" => PlainBench.Sieve(),
        _ => -1,
    };
}
