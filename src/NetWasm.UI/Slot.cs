using System;

namespace NetWasm.UI;

/// <summary>
/// Content passed from a parent into a component (Blazor's <c>RenderFragment</c>, Svelte's snippet).
/// The content is built where the component renders it (<c>@render(ChildContent)</c>), so it is owned by
/// the component's scope and torn down with it. Rendering a slot twice builds it twice.
/// </summary>
public sealed class Slot : Fragment
{
    private readonly Func<Content[]> build;

    public Slot(Func<Content[]> build)
    {
        this.build = build;
    }

    public Slot(Func<Content> build)
    {
        this.build = () => new[] { build() };
    }

    protected override void Apply(Element parent) => parent.Add(build());
}
