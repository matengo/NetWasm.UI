using System.Text;

namespace NetWasm.UI.Testing;

/// <summary>Preview. A read-only view of an element rendered by a <see cref="TestRenderer"/>; it reflects later updates.</summary>
public sealed class TestElement
{
    private readonly FakeDomHost.FakeNode document;

    internal TestElement(FakeDomHost.FakeNode node, FakeDomHost.FakeNode document)
    {
        Node = node;
        this.document = document;
    }

    internal FakeDomHost.FakeNode Node { get; }

    /// <summary>The tag name, e.g. <c>button</c>.</summary>
    public string Tag => Node.Tag;

    /// <summary>The element's namespace: <c>http://www.w3.org/2000/svg</c> for SVG elements, otherwise
    /// <c>http://www.w3.org/1999/xhtml</c> (as the browser's <c>namespaceURI</c>).</summary>
    public string NamespaceUri => Node.Svg ? "http://www.w3.org/2000/svg" : "http://www.w3.org/1999/xhtml";

    /// <summary>The attributes. Boolean attributes have an empty value.</summary>
    public IReadOnlyDictionary<string, string> Attributes => Node.Attributes;

    /// <summary>The attribute's value, or null when it is not set.</summary>
    public string? GetAttribute(string name) => Node.Attributes.TryGetValue(name, out var value) ? value : null;

    /// <summary>True when the attribute is set (any value).</summary>
    public bool HasAttribute(string name) => Node.Attributes.ContainsKey(name);

    /// <summary>A DOM property set with <c>Prop</c>/<c>@bind</c> (e.g. <c>value</c>, <c>checked</c>), or null.</summary>
    public object? GetProperty(string name) => Node.Properties.TryGetValue(name, out var value) ? value : null;

    /// <summary>The <c>value</c> property (as <see cref="TestRenderer.Input"/> or <c>@bind</c> set it), else the <c>value</c> attribute.</summary>
    public string? Value => GetProperty("value") as string ?? GetAttribute("value");

    /// <summary>The concatenated text of the element and its descendants (<c>textContent</c>).</summary>
    public string Text
    {
        get
        {
            var sb = new StringBuilder();
            foreach (var n in Node.Descendants())
            {
                if (n.Kind == "text")
                {
                    sb.Append(n.Text);
                }
            }

            return sb.ToString();
        }
    }

    /// <summary>The element's HTML, including itself.</summary>
    public string Html => Node.ToHtml();

    /// <summary>The child elements (not text or insertion points).</summary>
    public IReadOnlyList<TestElement> Children => Node.Children.Where(c => c.Kind == "element").Select(c => new TestElement(c, document)).ToArray();

    /// <summary>The parent element, or null for the root.</summary>
    public TestElement? Parent => Node.Parent is { } p && p != document ? new TestElement(p, document) : null;

    /// <summary>False once the element has been removed from the document.</summary>
    public bool IsConnected
    {
        get
        {
            for (var n = Node.Parent; n is not null; n = n.Parent)
            {
                if (n == document)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>The first descendant matching <paramref name="selector"/>; throws when there is none.</summary>
    public TestElement Find(string selector) =>
        FindAll(selector).FirstOrDefault()
        ?? throw new InvalidOperationException($"No element matches '{selector}' in:\n{Html}");

    /// <summary>Every descendant matching <paramref name="selector"/>, in document order.</summary>
    public IReadOnlyList<TestElement> FindAll(string selector) => Selector.Parse(selector).FindAll(Node, document);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is TestElement other && ReferenceEquals(other.Node, Node);

    /// <inheritdoc/>
    public override int GetHashCode() => Node.GetHashCode();

    /// <inheritdoc/>
    public override string ToString() => Html;
}
