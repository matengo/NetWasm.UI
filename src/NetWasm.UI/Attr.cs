using System;
using NetWasm.UI.Dom;
using NetWasm.UI.Reactive;

namespace NetWasm.UI;

/// <summary>An attribute or property applied to an element, optionally reactive.</summary>
public sealed class Attr : IContent
{
    private readonly Action<Element> apply;

    private Attr(Action<Element> apply)
    {
        this.apply = apply;
    }

    void IContent.ApplyTo(Element parent) => apply(parent);

    public static Attr Static(string name, string value) =>
        new(e => Renderer.SetAttribute(e.Id, name, value));

    public static Attr Dynamic(string name, Func<string?> value) =>
        new(e =>
        {
            var id = e.Id;
            string? last = null;
            _ = new Effect(() =>
            {
                // Only touch the DOM when the value changed; a fresh element has no attribute yet.
                var v = value();
                if (v == last)
                {
                    return;
                }

                last = v;
                if (v is null)
                {
                    Renderer.RemoveAttribute(id, name);
                }
                else
                {
                    Renderer.SetAttribute(id, name, v);
                }
            });
        });

    /// <summary>Boolean attribute such as <c>disabled</c>: present when true, absent when false.</summary>
    public static Attr Toggle(string name, Func<bool> value) =>
        Dynamic(name, () => value() ? string.Empty : null);

    /// <summary>Sets a JavaScript property (e.g. <c>value</c>) rather than an attribute.</summary>
    public static Attr Property(string name, Func<string> value) =>
        new(e =>
        {
            var id = e.Id;
            _ = new Effect(() => Renderer.SetProperty(id, name, value()));
        });

    public static Attr BoolProperty(string name, Func<bool> value) =>
        new(e =>
        {
            var id = e.Id;
            _ = new Effect(() => Renderer.SetBoolProperty(id, name, value()));
        });
}

/// <summary>An event listener. Registered with the owning scope so it is removed when the subtree goes away.</summary>
public sealed class Listener : IContent, IDisposable
{
    private readonly string eventName;
    private readonly Action<DomEvent> handler;
    private readonly ListenerFlags flags;
    private int handlerId;

    public Listener(string eventName, Action<DomEvent> handler, ListenerFlags flags = ListenerFlags.None)
    {
        this.eventName = eventName;
        this.handler = handler;
        this.flags = flags;
    }

    public int HandlerId => handlerId;

    void IContent.ApplyTo(Element parent)
    {
        handlerId = Renderer.AddListener(parent.Id, eventName, handler, flags);
        Scope.Current?.Register(this);
    }

    public void Dispose()
    {
        // The node is released by its scope, which drops the JS listener too; just forget the handler.
        Renderer.ForgetListener(handlerId);
    }
}
