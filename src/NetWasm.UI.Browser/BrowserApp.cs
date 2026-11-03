using NetWasm.UI.Dom;
using NetWasm.UI.Routing;

namespace NetWasm.UI.Browser;

/// <summary>Bootstraps a NetWasm.UI application in the browser.</summary>
public static class BrowserApp
{
    private static BrowserDomHost? host;

    /// <summary>
    /// Initializes the renderer against the browser DOM, installs a <see cref="Router"/> over the
    /// History API and mounts <paramref name="root"/>. <paramref name="selector"/> is resolved inside the
    /// DOM host's root (the document by default); an empty selector mounts directly into that root.
    /// </summary>
    /// <param name="root">The root component.</param>
    /// <param name="selector">Where to mount it.</param>
    /// <param name="basePath">
    /// The path the app is served under (e.g. <c>/my-app/</c>). When null, the DOM host's <c>basePath</c> option
    /// or the document's <c>&lt;base href&gt;</c> is used. Only the first call installs the router.
    /// </param>
    public static void Start(Component root, string selector = "#app", string? basePath = null)
    {
        if (host is null)
        {
            host = new BrowserDomHost();
            Renderer.Initialize(host);
            host.Connect();
            Router.Install(new BrowserHistory(), basePath ?? BrowserHistory.DocumentBasePath());
        }

        App.Mount(root, selector);
    }

    /// <summary>Unmounts everything <see cref="Start"/> mounted (see <see cref="App.Unmount"/>).</summary>
    public static void Stop() => App.Unmount();
}
