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

    /// <summary>A handle whose host node is released by someone else (e.g. its <see cref="TemplateInstance"/>).</summary>
    private protected Node(int id, bool owned)
    {
        Id = id;
        released = owned;
    }

    // Released ids are reused, so a node releases its id at most once. Template-hole handles start out released:
    // their TemplateInstance releases the ids.
    private bool released;

    public int Id { get; }

    void IContent.ApplyTo(Element parent) => Renderer.AppendChild(parent.Id, Id);

    /// <summary>Detaches the node from the DOM. The handle stays valid until the owning scope disposes it.</summary>
    public void Remove() => Renderer.Remove(Id);

    /// <summary>Releases the host-side handle. Called by the owning <see cref="Scope"/>; later calls do nothing.</summary>
    public virtual void Dispose()
    {
        if (released)
        {
            return;
        }

        released = true;
        Renderer.Release(Id);
    }
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
        _ = new TextBinding(Id, text);
    }
}

/// <summary>An invisible marker used by <see cref="Show"/> and <see cref="For{T, TKey}"/> to know where to insert.</summary>
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

    /// <summary>A handle to a template hole; the <see cref="TemplateInstance"/> releases the host node.</summary>
    internal Element(int id, string tag)
        : base(id, owned: true)
    {
        Tag = tag;
    }

    public string Tag { get; }

    /// <summary>Moves keyboard focus to this element (typically from <c>OnMount</c>).</summary>
    public void Focus() => Renderer.Focus(Id);

    /// <summary>
    /// Moves keyboard focus to the first tabbable descendant of this element, or to the element itself when it has
    /// none. Disabled, hidden, inert, invisible and <c>tabindex="-1"</c> elements are skipped. Use it from
    /// <c>OnMount</c> to focus a newly opened panel, or with sentinels to keep focus inside it.
    /// </summary>
    public void FocusFirst() => Renderer.FocusEdge(Id, last: false);

    /// <summary>Like <see cref="FocusFirst"/>, for the last tabbable descendant.</summary>
    public void FocusLast() => Renderer.FocusEdge(Id, last: true);

    /// <summary>
    /// Calls a DOM method on this element with string arguments, in order with the other DOM updates:
    /// <c>Call("requestFullscreen")</c>. Prefer the typed helpers (<see cref="ShowModal"/>, <see cref="ShowPopover"/>…).
    /// <paramref name="method"/> must come from your code, never from user input (see docs/security.md).
    /// </summary>
    public void Call(string method, params string[] args) => Renderer.Call(Id, method, args);

    /// <summary><c>dialog.showModal()</c>: opens a <c>&lt;dialog&gt;</c> in the top layer, making the rest of the page inert.</summary>
    public void ShowModal() => Renderer.Call(Id, "showModal", Array.Empty<string>());

    /// <summary><c>dialog.show()</c>: opens a <c>&lt;dialog&gt;</c> without making it modal.</summary>
    public void Show() => Renderer.Call(Id, "show", Array.Empty<string>());

    /// <summary><c>dialog.close(returnValue)</c>.</summary>
    public void Close(string? returnValue = null) =>
        Renderer.Call(Id, "close", returnValue is null ? Array.Empty<string>() : new[] { returnValue });

    /// <summary><c>showPopover()</c> for an element with a <c>popover</c> attribute.</summary>
    public void ShowPopover() => Renderer.Call(Id, "showPopover", Array.Empty<string>());

    /// <summary><c>hidePopover()</c>.</summary>
    public void HidePopover() => Renderer.Call(Id, "hidePopover", Array.Empty<string>());

    /// <summary><c>togglePopover()</c>.</summary>
    public void TogglePopover() => Renderer.Call(Id, "togglePopover", Array.Empty<string>());

    /// <summary>Removes keyboard focus from this element.</summary>
    public void Blur() => Renderer.Call(Id, "blur", Array.Empty<string>());

    /// <summary>Selects the text of an <c>&lt;input&gt;</c> or <c>&lt;textarea&gt;</c>.</summary>
    public void Select() => Renderer.Call(Id, "select", Array.Empty<string>());

    /// <summary><c>scrollIntoView({ block })</c>; <paramref name="block"/> is <c>start</c>, <c>center</c>, <c>end</c> or <c>nearest</c>.</summary>
    public void ScrollIntoView(string block = "nearest") => Renderer.Call(Id, "scrollIntoView", new[] { block });

    /// <summary>
    /// <c>getBoundingClientRect()</c>. Flushes pending DOM updates first. Call from <c>OnMount</c> or an event handler,
    /// not while building or inside a <c>Computed</c>/<c>Effect</c>. Throws <see cref="NotSupportedException"/> when the
    /// DOM host cannot read (see <see cref="IDomQueries"/>).
    /// </summary>
    public Rect GetRect() => Renderer.Queries().GetRect(Id);

    /// <summary>True when <paramref name="other"/> is this element or inside it. Same rules as <see cref="GetRect"/>.</summary>
    public bool Contains(Element other) => Renderer.Queries().Contains(Id, other.Id);

    /// <summary>True when this element has keyboard focus. Same rules as <see cref="GetRect"/>.</summary>
    public bool HasFocus() => Renderer.Queries().ActiveElementId() == Id;

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

    internal IContent? Inner => content;

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
