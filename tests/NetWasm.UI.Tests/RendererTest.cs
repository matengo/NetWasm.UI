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

    protected void Fire(FakeDomHost.FakeNode node, DomEvent e)
    {
        // A listener the node has through its template (a row of an @foreach) goes to handler 0, with the row's
        // first node id and the listener's slot before the payload, as dom.mjs sends it.
        foreach (var listener in node.RowListeners)
        {
            if (listener.Event == e.Type)
            {
                var payload = e.Encode();
                var bytes = new byte[5 + payload.Length];
                BitConverter.TryWriteBytes(bytes, listener.First);
                bytes[4] = (byte)listener.Slot;
                payload.CopyTo(bytes, 5);
                Renderer.Dispatch(Renderer.RowEventHandler, bytes);
                return;
            }
        }

        Renderer.Dispatch(Dom.HandlerFor(node, e.Type), e.Encode());
    }

    public virtual void Dispose()
    {
        Renderer.Reset();
        Scheduler.Reset();
    }
}
