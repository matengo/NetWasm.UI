using System;
using System.Net.Http;
using System.Runtime.InteropServices.JavaScript;
using System.Threading;
using System.Threading.Tasks;
using NetWasm.UI;
using NetWasm.UI.Browser;

namespace Hosted.Client;

public static partial class Program
{
    public static void Main()
    {
    }

    /// <summary>The page passes its origin: the API is served by the same ASP.NET Core app.</summary>
    [JSExport("start")]
    public static void Start(string origin)
    {
        var services = new Services()
            .Add(new HttpClient { BaseAddress = new Uri(origin + "/") })
            .AddLazy<WeatherApi>();
        BrowserApp.Start(new AppRoot(), services, "#app");
    }

    /// <summary>
    /// Called by publish-time prerendering (NetWasmUIPrerenderExport), which runs the app in Node without a page or a
    /// server. Requests never complete there, so pages that load data are prerendered in their loading state; the
    /// browser loads the data once the app has started.
    /// </summary>
    [JSExport("prerender")]
    public static void Prerender()
    {
        var services = new Services()
            .Add(new HttpClient(new PendingHandler()) { BaseAddress = new Uri("http://localhost/") })
            .AddLazy<WeatherApi>();
        BrowserApp.Start(new AppRoot(), services, "#app");
    }

    private sealed class PendingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            new TaskCompletionSource<HttpResponseMessage>().Task;
    }
}
