using NetWasm.UI.Dom;
using NetWasm.UI.Reactive;

namespace NetWasm.UI;

/// <summary>Application entry points.</summary>
public static class App
{
    /// <summary>
    /// Builds <paramref name="root"/>, mounts it under the element matching <paramref name="selector"/>
    /// and pushes the initial batch to the host.
    /// </summary>
    public static void Mount(Component root, string selector)
    {
        if (!Renderer.IsInitialized)
        {
            throw new System.InvalidOperationException("Call Renderer.Initialize(host) before App.Mount.");
        }

        Scheduler.Batch(() =>
        {
            var node = root.Render();
            Renderer.Mount(node.Id, selector);
        });
    }
}
