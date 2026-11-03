using System;
using System.Net.Http;
using System.Runtime.InteropServices.JavaScript;
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
        var api = new Api(new HttpClient { BaseAddress = new Uri(origin + "/") });
        BrowserApp.Start(new UsersPage { Api = api }, "#app");
    }
}
