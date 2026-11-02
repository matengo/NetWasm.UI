using System;
using System.Collections.Generic;
using NetWasm.UI.Reactive;

namespace NetWasm.UI.Dom;

/// <summary>
/// The renderer: allocates node ids, encodes ops into the current batch, owns the event handler
/// registry and pushes exactly one batch to the host per flush.
/// </summary>
public static class Renderer
{
    private static IDomHost? host;
    private static readonly BatchWriter batch = new();
    private static readonly Dictionary<int, Listener> handlers = new();
    private static int nextNodeId = 1;
    private static int nextHandlerId = 1;

    public static bool IsInitialized => host is not null;

    /// <summary>Number of batches pushed to the host. Useful for asserting the "one crossing per update" rule.</summary>
    public static int FlushCount { get; private set; }

    public static void Initialize(IDomHost domHost)
    {
        host = domHost;
        Scheduler.Flushed -= Flush;
        Scheduler.Flushed += Flush;
    }

    /// <summary>Resets all renderer state. Intended for tests.</summary>
    public static void Reset()
    {
        Scheduler.Flushed -= Flush;
        host = null;
        batch.Clear();
        handlers.Clear();
        nextNodeId = 1;
        nextHandlerId = 1;
        FlushCount = 0;
    }

    /// <summary>Sends the pending ops to the host, if any.</summary>
    public static void Flush()
    {
        if (host is null || batch.IsEmpty)
        {
            return;
        }

        FlushCount++;
        host.ApplyBatch(batch.Take());
    }

    internal static int AllocateNodeId() => nextNodeId++;

    // ---- node creation ----

    public static int CreateElement(string tag)
    {
        var id = AllocateNodeId();
        batch.Op(Op.CreateElement);
        batch.Int(id);
        batch.String(tag);
        return id;
    }

    public static int CreateText(string text)
    {
        var id = AllocateNodeId();
        batch.Op(Op.CreateText);
        batch.Int(id);
        batch.String(text);
        return id;
    }

    public static int CreateAnchor()
    {
        var id = AllocateNodeId();
        batch.Op(Op.CreateAnchor);
        batch.Int(id);
        return id;
    }

    // ---- mutation ----

    public static void SetText(int id, string text)
    {
        batch.Op(Op.SetText);
        batch.Int(id);
        batch.String(text);
    }

    public static void SetAttribute(int id, string name, string value)
    {
        batch.Op(Op.SetAttribute);
        batch.Int(id);
        batch.String(name);
        batch.String(value);
    }

    public static void RemoveAttribute(int id, string name)
    {
        batch.Op(Op.RemoveAttribute);
        batch.Int(id);
        batch.String(name);
    }

    public static void SetProperty(int id, string name, string value)
    {
        batch.Op(Op.SetProperty);
        batch.Int(id);
        batch.String(name);
        batch.String(value);
    }

    public static void SetBoolProperty(int id, string name, bool value)
    {
        batch.Op(Op.SetBoolProperty);
        batch.Int(id);
        batch.String(name);
        batch.Bool(value);
    }

    // ---- tree ----

    public static void AppendChild(int parent, int child)
    {
        batch.Op(Op.AppendChild);
        batch.Int(parent);
        batch.Int(child);
    }

    public static void InsertBefore(int reference, int child)
    {
        batch.Op(Op.InsertBefore);
        batch.Int(reference);
        batch.Int(child);
    }

    public static void Remove(int id)
    {
        batch.Op(Op.Remove);
        batch.Int(id);
    }

    public static void Release(int id)
    {
        batch.Op(Op.Release);
        batch.Int(id);
    }

    public static void Mount(int id, string selector)
    {
        batch.Op(Op.Mount);
        batch.Int(id);
        batch.String(selector);
    }

    public static void Focus(int id)
    {
        batch.Op(Op.Focus);
        batch.Int(id);
    }

    // ---- events ----

    public static int AddListener(int id, string eventName, Action<DomEvent> handler, ListenerFlags flags)
    {
        var handlerId = nextHandlerId++;
        handlers[handlerId] = new Listener(eventName, handler, Tracker.CurrentOwner);
        batch.Op(Op.AddListener);
        batch.Int(id);
        batch.String(eventName);
        batch.Int(handlerId);
        batch.Byte((byte)flags);
        return handlerId;
    }

    public static void RemoveListener(int id, int handlerId)
    {
        if (handlers.Remove(handlerId))
        {
            batch.Op(Op.RemoveListener);
            batch.Int(id);
            batch.Int(handlerId);
        }
    }

    /// <summary>Forgets a handler without emitting an op (the node is being released anyway).</summary>
    internal static void ForgetListener(int handlerId) => handlers.Remove(handlerId);

    /// <summary>
    /// Entry point for events coming from the host. Runs the handler inside a batch so all
    /// resulting DOM ops are pushed in a single <see cref="IDomHost.ApplyBatch"/> call.
    /// </summary>
    public static void Dispatch(int handlerId, byte[] payload)
    {
        if (!handlers.TryGetValue(handlerId, out var listener))
        {
            return;
        }

        Invoke(listener, DomEvent.Decode(listener.EventName, payload));
    }

    /// <summary>Dispatches an already-decoded event (tests, fake hosts).</summary>
    public static void Dispatch(int handlerId, DomEvent e)
    {
        if (handlers.TryGetValue(handlerId, out var listener))
        {
            Invoke(listener, e);
        }
    }

    // Errors go to the nearest ErrorBoundary around the element (or App.OnError) inside the batch,
    // so the DOM ops queued before the throw still reach the host.
    private static void Invoke(Listener listener, DomEvent e) => Scheduler.Batch(() =>
    {
        try
        {
            listener.Handler(e);
        }
        catch (Exception ex)
        {
            ErrorHandling.Report(ex, listener.Owner);
        }
    });

    private readonly struct Listener
    {
        public Listener(string eventName, Action<DomEvent> handler, Scope? owner)
        {
            EventName = eventName;
            Handler = handler;
            Owner = owner;
        }

        public string EventName { get; }
        public Action<DomEvent> Handler { get; }
        public Scope? Owner { get; }
    }
}
