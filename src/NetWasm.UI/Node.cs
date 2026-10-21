using System;
using NetWasm.UI.Dom;
using NetWasm.UI.Reactive;

namespace NetWasm.UI;

/// <summary>Anything that can appear in an element's content list.</summary>
public interface IContent
{
    void ApplyTo(Element parent);
}

/// <summary>Content that controls a region of the parent (conditional or list rendering) rather than being a single node.</summary>
public abstract class Fragment : IContent
{
    void IContent.ApplyTo(Element parent) => Apply(parent);

    protected abstract void Apply(Element parent);
}

/// <summary>A DOM node handle. Creating a node emits its creation op immediately.</summary>
public abstract class Node : IContent, IDisposable
{
    protected Node(int id)
    {
        Id = id;
        Scope.Current?.Register(this);
    }

    public int Id { get; }

    void IContent.ApplyTo(Element parent) => Renderer.AppendChild(parent.Id, Id);

    /// <summary>Detaches the node from the DOM. The handle stays valid until the owning scope disposes it.</summary>
    public void Remove() => Renderer.Remove(Id);

    /// <summary>Releases the host-side handle. Called by the owning <see cref="Scope"/>.</summary>
    public virtual void Dispose() => Renderer.Release(Id);
}

/// <summary>A text node whose content is either static or bound to a reactive expression.</summary>
public sealed class TextNode : Node
{
    public TextNode(string text)
        : base(Renderer.CreateText(text))
    {
    }

    public TextNode(Func<string> text)
        : base(Renderer.CreateText(string.Empty))
    {
        var id = Id;
        _ = new Effect(() => Renderer.SetText(id, text()));
    }
}

/// <summary>An invisible marker used by <see cref="Show"/> and <see cref="For{T}"/> to know where to insert.</summary>
public sealed class Anchor : Node
{
    public Anchor()
        : base(Renderer.CreateAnchor())
    {
    }
}

public sealed class Element : Node
{
    public Element(string tag, params Content[] content)
        : base(Renderer.CreateElement(tag))
    {
        Tag = tag;
        for (var i = 0; i < content.Length; i++)
        {
            content[i].ApplyTo(this);
        }
    }

    public string Tag { get; }

    /// <summary>Moves keyboard focus to this element (typically from <c>OnMount</c>).</summary>
    public void Focus() => Renderer.Focus(Id);

    public Element Add(params Content[] content)
    {
        for (var i = 0; i < content.Length; i++)
        {
            content[i].ApplyTo(this);
        }

        return this;
    }
}

/// <summary>
/// Union of the things an element can contain: static text, nodes, attributes, listeners and components.
/// Strings convert implicitly so markup-like code stays terse.
/// </summary>
public readonly struct Content
{
    private readonly string? text;
    private readonly IContent? content;

    private Content(string text)
    {
        this.text = text;
        content = null;
    }

    private Content(IContent content)
    {
        text = null;
        this.content = content;
    }

    public static implicit operator Content(string text) => new(text);
    public static implicit operator Content(Node node) => new(node);
    public static implicit operator Content(Attr attr) => new(attr);
    public static implicit operator Content(Listener listener) => new(listener);
    public static implicit operator Content(Component component) => new(component);
    public static implicit operator Content(Fragment? fragment) => new(fragment!);

    public static Content Of(IContent content) => new(content);

    internal void ApplyTo(Element parent)
    {
        if (text is not null)
        {
            Renderer.AppendChild(parent.Id, new TextNode(text).Id);
        }
        else
        {
            // Null content (e.g. an unset optional Slot) renders nothing.
            content?.ApplyTo(parent);
        }
    }
}
