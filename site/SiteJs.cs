using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;

namespace Site;

/// <summary>The [JSImport] declarations for site.interop.mjs.</summary>
internal static class SiteJs
{
    private const string Module = "site.interop";

    [JSImport("copyText", Module)]
    public static extern void CopyText(string text);

    [JSImport("fetchText", Module)]
    public static extern Task<string> FetchText(string path);

    [JSImport("pageBytes", Module)]
    public static extern int PageBytes();

    [JSImport("wasmBytes", Module)]
    public static extern int WasmBytes();

    [JSImport("reducedMotion", Module)]
    public static extern bool ReducedMotion();

    [JSImport("setTitle", Module)]
    public static extern void SetTitle(string title);
}
