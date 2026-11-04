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
    public static void Mount(Component root, string selector) => MountCore(root, selector, null);

    /// <summary>
    /// Like <see cref="Mount(Component, string)"/>, with app-wide <paramref name="services"/> that every
    /// component can <see cref="Html.Inject{T}"/>. They live until <see cref="Unmount"/>.
    /// </summary>
    public static void Mount(Component root, string selector, Services services) =>
        MountCore(root, selector, services ?? throw new System.ArgumentNullException(nameof(services)));

    private static void MountCore(Component root, string selector, Services? services)
    {
        if (!Renderer.IsInitialized)
        {
            throw new System.InvalidOperationException("Call Renderer.Initialize(host) before App.Mount.");
        }

        Scheduler.Batch(() =>
        {
            // The root scope holds the app services; the component's own scope is its child.
            var scope = new Scope((Scope?)null);
            services?.ApplyTo(scope);
            mounted.Add(new MountedRoot(root, scope));
            try
            {
                var node = scope.Run(root.Render);
                Renderer.Mount(node.Id, selector);
            }
            catch (System.Exception ex)
            {
                ErrorHandling.Report(ex, null);
            }
        });
    }

    private readonly struct MountedRoot
    {
        public MountedRoot(Component component, Scope scope)
        {
            Component = component;
            Scope = scope;
        }

        public Component Component { get; }

        public Scope Scope { get; }
    }

    private static readonly System.Collections.Generic.List<MountedRoot> mounted = new();

    internal static void ForgetMounted() => mounted.Clear();

    /// <summary>
    /// Removes every component mounted with <see cref="Mount(Component, string)"/> from the DOM and disposes their effects,
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
                roots[i].Component.Dispose();
                roots[i].Scope.Dispose();
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
