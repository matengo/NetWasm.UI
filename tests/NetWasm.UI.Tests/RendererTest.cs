using NetWasm.UI.Dom;
using NetWasm.UI.Reactive;

namespace NetWasm.UI.Tests;

/// <summary>Resets global renderer/scheduler state and wires a fresh <see cref="FakeDomHost"/> per test.</summary>
public abstract class RendererTest : IDisposable
{
    protected FakeDomHost Dom { get; }

    protected RendererTest()
    {
        Scheduler.Reset();
        Renderer.Reset();
        Dom = new FakeDomHost();
        Renderer.Initialize(Dom);
    }

    /// <summary>Simulates the browser dispatching an event to a handler, going through the binary payload path.</summary>
    protected void Fire(FakeDomHost.FakeNode node, string eventName, string value = "") =>
        Fire(node, new DomEvent(eventName, value));

    protected void Fire(FakeDomHost.FakeNode node, DomEvent e) =>
        Renderer.Dispatch(Dom.HandlerFor(node, e.Type), e.Encode());

    public virtual void Dispose()
    {
        Renderer.Reset();
        Scheduler.Reset();
    }
}
