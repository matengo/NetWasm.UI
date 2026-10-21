using NetWasm.UI.Dom;
using NetWasm.UI.Routing;

namespace NetWasm.UI.Browser;

/// <summary>Bootstraps a NetWasm.UI application in the browser.</summary>
public static class BrowserApp
{
    private static BrowserDomHost? host;

    /// <summary>
    /// Initializes the renderer against the browser DOM, installs a <see cref="Router"/> over the
    /// History API and mounts <paramref name="root"/>.
    /// </summary>
    public static void Start(Component root, string selector = "#app")
    {
        if (host is null)
        {
            host = new BrowserDomHost();
            Renderer.Initialize(host);
            host.Connect();
            Router.Install(new BrowserHistory());
        }

        App.Mount(root, selector);
    }
}
