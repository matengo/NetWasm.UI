using System;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;

namespace Interop;

/// <summary>The [JSImport] declarations for meter.mjs, registered as module "interop.meter" in Interop.csproj.</summary>
internal static class MeterJs
{
    private const string Module = "interop.meter";

    [JSImport("load", Module)]
    public static extern Task Load();

    [JSImport("create", Module)]
    public static extern JSObject Create(string selector, int max);

    [JSImport("setValue", Module)]
    public static extern void SetValue(JSObject meter, int value);

    [JSImport("destroy", Module)]
    public static extern void Destroy(JSObject meter);

    [JSImport("onChange", Module)]
    public static extern JSSubscription OnChange(JSObject meter, Action<int> handler);
}