using System;
using NetWasm.UI.Dom;
using NetWasm.UI.Reactive;

namespace NetWasm.UI;

/// <summary>An attribute or property applied to an element, optionally reactive.</summary>
public sealed class Attr : IContent
{
    private readonly Action<Element>? apply;
    private readonly string? name;
    private readonly string? value;
    private readonly Func<string?>? dynamic;

    private Attr(Action<Element> apply)
    {
        this.apply = apply;
    }

    private Attr(string name, string? value, Func<string?>? dynamic)
    {
        this.name = name;
        this.value = value;
        this.dynamic = dynamic;
    }

    void IContent.ApplyTo(Element parent)
    {
        if (apply is not null)
        {
            apply(parent);
        }
        else if (dynamic is not null)
        {
            _ = new AttrBinding(parent.Id, name!, dynamic);
        }
        else
        {
            Renderer.SetAttribute(parent.Id, name!, value!);
        }
    }

    public static Attr Static(string name, string value) => new(name, value, null);

    public static Attr Dynamic(string name, Func<string?> value) => new(name, null, value);

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
    private readonly Action<DomEvent>? handler;
    private readonly Action? simple;
    private readonly ListenerFlags flags;
    private int handlerId;

    public Listener(string eventName, Action<DomEvent> handler, ListenerFlags flags = ListenerFlags.None)
    {
        EventName = eventName;
        this.handler = handler;
        this.flags = flags;
    }

    /// <summary>A listener that doesn't need the event data (no wrapper delegate).</summary>
    public Listener(string eventName, Action handler, ListenerFlags flags = ListenerFlags.None)
    {
        EventName = eventName;
        simple = handler;
        this.flags = flags;
    }

    public int HandlerId => handlerId;

    internal string EventName { get; }

    /// <summary>The scope that was current when the listener was attached; errors are reported there.</summary>
    internal Scope? Owner { get; set; }

    internal void Invoke(DomEvent e)
    {
        if (simple is not null)
        {
            simple();
        }
        else
        {
            handler!(e);
        }
    }

    void IContent.ApplyTo(Element parent)
    {
        Owner = Scope.Current;
        handlerId = Renderer.AddListener(parent.Id, this, flags);
        Owner?.Register(this);
    }

    public void Dispose()
    {
        // The node is released by its scope, which drops the JS listener too; just forget the handler.
        Renderer.ForgetListener(handlerId);
    }
}
