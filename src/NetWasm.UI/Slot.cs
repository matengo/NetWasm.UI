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

/// <summary>
/// A slot that takes an argument (Blazor's <c>RenderFragment&lt;T&gt;</c>): a parent passes a template for each item,
/// the component renders it with <c>@render(Cell, row)</c> or <c>Cell.With(row)</c>. In a template, fill it with
/// <c>&lt;template slot="Cell" context="row"&gt;…&lt;/template&gt;</c>.
/// </summary>
public sealed class Slot<T>
{
    private readonly Func<T, Content[]> build;

    public Slot(Func<T, Content[]> build)
    {
        this.build = build ?? throw new ArgumentNullException(nameof(build));
    }

    public Slot(Func<T, Content> build)
    {
        if (build is null)
        {
            throw new ArgumentNullException(nameof(build));
        }

        this.build = arg => new[] { build(arg) };
    }

    /// <summary>The slot's content for <paramref name="arg"/>; built where it is inserted.</summary>
    public Fragment With(T arg) => new Slot(() => build(arg));
}
