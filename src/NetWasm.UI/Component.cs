using System;
using NetWasm.UI.Reactive;

namespace NetWasm.UI;

/// <summary>
/// Base class for components. <see cref="Build"/> runs exactly once; reactivity comes from
/// signals read inside <c>Text(() => ...)</c>, <c>Show</c>, <c>For</c> and reactive attributes.
/// </summary>
public abstract class Component : IContent, IDisposable
{
    private Scope? scope;
    private Node? root;

    /// <summary>The root node, available after the component has been built.</summary>
    public Node Root => root ?? throw new InvalidOperationException("Component has not been built yet.");

    /// <summary>
    /// HTML attributes and listeners given on the component tag that are not properties (lowercase or dashed
    /// names such as <c>class</c>, <c>aria-label</c>, <c>data-x</c>, and <c>@on…</c> handlers), plus any
    /// <c>@attributes</c>. Apply them to an element with <c>@attributes="Attributes"</c> in a template or
    /// <see cref="Html.Splat(Content[], string?, string?)"/> in code.
    /// </summary>
    public Content[] Attributes { get; set; } = Array.Empty<Content>();

    protected abstract Node Build();

    /// <summary>Builds the component inside its own scope.</summary>
    public Node Render()
    {
        if (root is not null)
        {
            return root;
        }

        scope = new Scope();
        root = scope.Run(() => Tracker.Untracked(Build));
        return root;
    }

    void IContent.ApplyTo(Element parent) => Dom.Renderer.AppendChild(parent.Id, Render().Id);

    public void Dispose()
    {
        root?.Remove();
        scope?.Dispose();
        scope = null;
        root = null;
    }
}
