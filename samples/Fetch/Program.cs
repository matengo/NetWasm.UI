using System;
using System.Net.Http;
using System.Runtime.InteropServices.JavaScript;
using NetWasm.UI;
using NetWasm.UI.Browser;

namespace Fetch;

public static partial class Program
{
    public static void Main()
    {
    }

    /// <summary>The page passes its origin so relative API paths resolve.</summary>
    [JSExport("start")]
    public static void Start(string origin)
    {
        // AddLazy<Api>() is wired at compile time: the generator emits () => new Api(Inject<HttpClient>()).
        var services = new Services()
            .Add(new HttpClient { BaseAddress = new Uri(origin + "/") })
            .AddLazy<Api>();
        BrowserApp.Start(new UsersPage(), services, "#app");
    }
}
