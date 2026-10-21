using NetWasm.UI.Dom;

namespace NetWasm.UI.Browser;

/// <summary>Bootstraps a NetWasm.UI application in the browser.</summary>
public static class BrowserApp
{
    private static BrowserDomHost? host;

    /// <summary>Initializes the renderer against the browser DOM and mounts <paramref name="root"/>.</summary>
    public static void Start(Component root, string selector = "#app")
    {
        if (host is null)
        {
            host = new BrowserDomHost();
            Renderer.Initialize(host);
            host.Connect();
        }

        App.Mount(root, selector);
    }
}
