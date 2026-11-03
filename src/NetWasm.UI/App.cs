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
            try
            {
                var node = root.Render();
                mounted.Add(root);
                Renderer.Mount(node.Id, selector);
            }
            catch (System.Exception ex)
            {
                ErrorHandling.Report(ex, null);
            }
        });
    }

    private static readonly System.Collections.Generic.List<Component> mounted = new();

    internal static void ForgetMounted() => mounted.Clear();

    /// <summary>
    /// Removes every component mounted with <see cref="Mount"/> from the DOM and disposes their effects,
    /// listeners and nodes. Call it before closing a Wasm instance that should leave the page clean.
    /// </summary>
    public static void Unmount()
    {
        if (mounted.Count == 0)
        {
            return;
        }

        Scheduler.Batch(() =>
        {
            var roots = mounted.ToArray();
            mounted.Clear();
            for (var i = roots.Length - 1; i >= 0; i--)
            {
                roots[i].Dispose();
            }
        });
    }

    /// <summary>
    /// Receives errors that no <see cref="ErrorBoundary"/> handled: from effects, event handlers,
    /// <c>OnMount</c>/<c>OnCleanup</c> callbacks and the initial render. When unset, such an error is
    /// rethrown after the current update has been flushed to the host (it shows up in the browser console).
    /// </summary>
    public static System.Action<System.Exception>? OnError
    {
        get => ErrorHandling.Global;
        set => ErrorHandling.Global = value;
    }
}
