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

    /// <summary>Simulates the browser dispatching an event to a handler.</summary>
    protected void Fire(FakeDomHost.FakeNode node, string eventName, string payload = "") =>
        Renderer.Dispatch(Dom.HandlerFor(node, eventName), payload);

    public void Dispose()
    {
        Renderer.Reset();
        Scheduler.Reset();
    }
}
