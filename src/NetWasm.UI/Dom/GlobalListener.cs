using System;
using NetWasm.UI.Reactive;

namespace NetWasm.UI.Dom;

/// <summary>
/// A document/window listener or resize observation owned by the current component scope or effect run
/// (removed when it is disposed). Created by <c>Html.OnDocument</c>, <c>Html.OnWindow</c> and <c>Html.OnResize</c>.
/// </summary>
internal sealed class GlobalListener : IDisposable
{
    private int handlerId;

    private GlobalListener()
    {
    }

    internal static GlobalListener Add(GlobalTarget target, string eventName, Action<DomEvent> handler, ListenerFlags flags, Element? within)
    {
        if (string.IsNullOrEmpty(eventName))
        {
            throw new ArgumentException("An event name is required.", nameof(eventName));
        }

        if (handler is null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        var listener = new UI.Listener(eventName, handler, flags) { Owner = Tracker.CurrentOwner };
        var result = new GlobalListener();
        result.handlerId = Renderer.AddGlobalListener(target, listener, flags, within?.Id ?? 0);
        result.Own();
        return result;
    }

    internal static GlobalListener Resize(Element element, Action<Rect> handler)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        if (handler is null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        var listener = new UI.Listener("resize", () => handler(element.GetRect())) { Owner = Tracker.CurrentOwner };
        var result = new GlobalListener();
        result.handlerId = Renderer.ObserveResize(element.Id, listener);
        result.Own();
        return result;
    }

    /// <summary>Ties the registration to the current scope/effect; outside them (an event handler) the caller disposes it.</summary>
    private void Own()
    {
        if (Tracker.Current is Effect || Tracker.CurrentOwner is not null)
        {
            Lifecycle.OnCleanup(Dispose);
        }
    }

    public void Dispose()
    {
        if (handlerId != 0)
        {
            Renderer.RemoveGlobalListener(handlerId);
            handlerId = 0;
        }
    }
}
