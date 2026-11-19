using NetWasm.UI.Dom;
using NetWasm.UI.Reactive;
using NetWasm.UI.Routing;

namespace NetWasm.UI.Testing;

/// <summary>
/// Preview. Renders a component into an in-memory DOM on .NET, with no browser, for fast unit tests:
/// <code>
/// using var r = TestRenderer.Render(new Counter());
/// r.Click(r.Find("button"));
/// Assert.Equal("1", r.Find("output").Text);
/// </code>
/// Every action runs as one update and returns after the DOM is up to date. There is no CSS, layout, real focus
/// handling or animation (exit animations remove at once); use browser tests for those.
/// </summary>
/// <remarks>
/// The renderer is global, so one <see cref="TestRenderer"/> is live at a time: <see cref="Render"/> waits until the
/// previous one is disposed. Dispose it (<c>using var</c>) at the end of each test.
/// </remarks>
public sealed class TestRenderer : IDisposable
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(30);

    private readonly FakeDomHost dom = new();
    private bool disposed;

    private TestRenderer()
    {
    }

    /// <summary>Mounts <paramref name="root"/>, with optional app <paramref name="services"/>, and returns once it has rendered.</summary>
    public static TestRenderer Render(Component root, Services? services = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (!Gate.Wait(GateTimeout))
        {
            throw new InvalidOperationException(
                "Another TestRenderer is still live. Dispose each one (using var r = TestRenderer.Render(...)).");
        }

        var renderer = new TestRenderer();
        try
        {
            ResetGlobals();
            Renderer.Initialize(renderer.dom);
            if (services is null)
            {
                App.Mount(root, "#app");
            }
            else
            {
                App.Mount(root, "#app", services);
            }
        }
        catch
        {
            renderer.Dispose();
            throw;
        }

        return renderer;
    }

    /// <summary>The rendered HTML. Insertion points (empty <c>@if</c>s, list ends) show as <c>&lt;!----&gt;</c>.</summary>
    public string Html => dom.Html;

    /// <summary>The focused element: set by <c>Html.Focus</c>, <c>Element.Focus</c>/<c>FocusFirst</c>/<c>FocusLast</c> and <see cref="Focus"/>.</summary>
    public TestElement? Focused => dom.Nodes.TryGetValue(dom.ActiveId, out var node) && node.Kind == "element" ? new TestElement(node, dom.Document) : null;

    /// <summary>What <c>Html.ViewportRect</c> returns. Defaults to 1024 × 768.</summary>
    public Rect Viewport
    {
        get => dom.ViewportRect;
        set => dom.ViewportRect = value;
    }

    /// <summary>The first element matching <paramref name="selector"/>; throws when there is none.</summary>
    /// <param name="selector">Tag, <c>#id</c>, <c>.class</c>, <c>[attr]</c>, <c>[attr=value]</c>, compounds of these and the descendant combinator (a space).</param>
    public TestElement Find(string selector) =>
        FindAll(selector).FirstOrDefault()
        ?? throw new InvalidOperationException($"No element matches '{selector}' in:\n{Html}");

    /// <summary>Every element matching <paramref name="selector"/>, in document order.</summary>
    public IReadOnlyList<TestElement> FindAll(string selector) => Selector.Parse(selector).FindAll(dom.Document, dom.Document);

    /// <summary>A primary-button click on <paramref name="element"/>.</summary>
    public void Click(TestElement element) => Dispatch(element, new DomEvent("click"));

    /// <summary>Sets the element's value and dispatches <c>input</c>, as typing does.</summary>
    public void Input(TestElement element, string value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.Node.Properties["value"] = value;
        Dispatch(element, new DomEvent("input", value));
    }

    /// <summary>Sets the element's value and dispatches <c>change</c>, as committing a select or text field does.</summary>
    public void Change(TestElement element, string value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.Node.Properties["value"] = value;
        Dispatch(element, new DomEvent("change", value));
    }

    /// <summary>Dispatches <c>keydown</c> for <paramref name="key"/> (a <c>KeyboardEvent.key</c> value such as <c>Enter</c> or <c>ArrowDown</c>).</summary>
    public void KeyDown(TestElement element, string key, EventModifiers modifiers = EventModifiers.None) =>
        Dispatch(element, new DomEvent("keydown", key: key, modifiers: modifiers));

    /// <summary>Moves focus to <paramref name="element"/> and dispatches <c>focusin</c> on it.</summary>
    public void Focus(TestElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        dom.ActiveId = element.Node.Id;
        Dispatch(element, new DomEvent("focusin"));
    }

    /// <summary>
    /// Dispatches <paramref name="e"/> at <paramref name="element"/>. It goes through capture listeners from the
    /// document down, then bubbles up to the element's ancestors and to document and window listeners, stopping
    /// where a listener has <c>:stopPropagation</c>. <c>TargetId</c> and, for listeners registered with
    /// <c>within</c>, <c>Inside</c> are filled in.
    /// </summary>
    public void Dispatch(TestElement element, DomEvent e)
    {
        ArgumentNullException.ThrowIfNull(element);
        ThrowIfDisposed();
        var target = element.Node;
        if (!element.IsConnected)
        {
            throw new InvalidOperationException($"<{target.Tag}> is no longer in the document.");
        }

        Scheduler.Batch(() =>
        {
            var path = new List<FakeDomHost.FakeNode>();
            for (var n = target; n is not null && n != dom.Document; n = n.Parent)
            {
                path.Add(n);
            }

            var targeted = e with { TargetId = target.Id };
            for (var i = path.Count - 1; i >= 0; i--)
            {
                if (Deliver(path[i], targeted, capture: true))
                {
                    return;
                }
            }

            foreach (var node in path)
            {
                if (Deliver(node, targeted, capture: false))
                {
                    return;
                }
            }

            DeliverGlobal(GlobalTarget.Document, targeted, target);
            DeliverGlobal(GlobalTarget.Window, targeted, target);
        });
    }

    /// <summary>Dispatches <paramref name="e"/> to the document's listeners (<c>Html.OnDocument</c>), as if it targeted the document itself.</summary>
    public void DispatchDocument(DomEvent e)
    {
        ThrowIfDisposed();
        Scheduler.Batch(() => DeliverGlobal(GlobalTarget.Document, e, null));
    }

    /// <summary>Dispatches <paramref name="e"/> to the window's listeners (<c>Html.OnWindow</c>), e.g. <c>resize</c> or <c>scroll</c>.</summary>
    public void DispatchWindow(DomEvent e)
    {
        ThrowIfDisposed();
        Scheduler.Batch(() => DeliverGlobal(GlobalTarget.Window, e, null));
    }

    /// <summary>Sets what <c>GetRect</c> returns for <paramref name="element"/> and notifies its resize observers.</summary>
    public void SetRect(TestElement element, Rect rect)
    {
        ArgumentNullException.ThrowIfNull(element);
        ThrowIfDisposed();
        var id = element.Node.Id;
        dom.Rects[id] = rect;
        var observers = dom.ResizeObservers.Where(o => o.Value == id).Select(o => o.Key).ToArray();
        Scheduler.Batch(() =>
        {
            foreach (var handlerId in observers)
            {
                Renderer.Dispatch(handlerId, Array.Empty<byte>());
            }
        });
    }

    /// <summary>Unmounts the component and releases the renderer for the next test.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        try
        {
            if (Renderer.IsInitialized)
            {
                App.Unmount();
            }
        }
        finally
        {
            ResetGlobals();
            Gate.Release();
        }
    }

    private static void ResetGlobals()
    {
        Router.Reset();
        Renderer.Reset();
        Scheduler.Reset();
        ErrorHandling.Reset();
    }

    // Returns true when a listener on the node stops propagation.
    private bool Deliver(FakeDomHost.FakeNode node, DomEvent e, bool capture)
    {
        var stop = false;
        foreach (var (handlerId, (eventName, flags)) in node.Listeners.ToArray())
        {
            if (eventName == e.Type && ((flags & (byte)ListenerFlags.Capture) != 0) == capture)
            {
                Renderer.Dispatch(handlerId, e);
                stop |= (flags & (byte)ListenerFlags.StopPropagation) != 0;
            }
        }

        if (!capture)
        {
            foreach (var (eventName, slot, flags, first) in node.RowListeners.ToArray())
            {
                if (eventName == e.Type)
                {
                    Renderer.DispatchRow(first, slot, e);
                    stop |= (flags & (byte)ListenerFlags.StopPropagation) != 0;
                }
            }
        }

        return stop;
    }

    private void DeliverGlobal(GlobalTarget globalTarget, DomEvent e, FakeDomHost.FakeNode? target)
    {
        foreach (var (handlerId, (listenerTarget, eventName, _, withinId)) in dom.GlobalListeners.ToArray())
        {
            if (listenerTarget == globalTarget && eventName == e.Type)
            {
                var inside = withinId != 0 && target is not null && IsWithin(target, withinId);
                Renderer.Dispatch(handlerId, e with { Inside = inside });
            }
        }
    }

    private bool IsWithin(FakeDomHost.FakeNode node, int ancestorId)
    {
        if (!dom.Nodes.TryGetValue(ancestorId, out var ancestor))
        {
            return false;
        }

        for (var n = node; n is not null; n = n.Parent)
        {
            if (ReferenceEquals(n, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}
