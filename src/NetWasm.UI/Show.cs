using System;
using NetWasm.UI.Dom;
using NetWasm.UI.Reactive;

namespace NetWasm.UI;

/// <summary>
/// Conditional rendering. The branch is built inside a child scope when the condition becomes true
/// and the scope is disposed (removing nodes, effects and listeners) when it becomes false.
/// </summary>
public sealed class Show : Fragment
{
    private readonly Func<bool> condition;
    private readonly Func<Node> render;
    private readonly Func<Node>? fallback;
    private bool animateExit;

    public Show(Func<bool> condition, Func<Node> render, Func<Node>? fallback = null)
    {
        this.condition = condition;
        this.render = render;
        this.fallback = fallback;
    }

    /// <summary>
    /// Lets the content play an exit animation before it leaves the DOM. A branch that goes away gets
    /// <c>data-state="closed"</c> and stays until the animations and transitions that starts (on it or inside it)
    /// have finished; one that comes in gets <c>data-state="open"</c>. To C# the old branch is gone at once: its
    /// effects and listeners are disposed, and the next branch is built straight away.
    /// </summary>
    public Show AnimateExit()
    {
        animateExit = true;
        return this;
    }

    /// <summary>Detaches <paramref name="node"/>, after its exit animation when <paramref name="animate"/> is set.</summary>
    internal static void Leave(Node node, bool animate)
    {
        if (animate)
        {
            Renderer.RemoveAnimated(node.Id);
        }
        else
        {
            node.Remove();
        }
    }

    /// <summary>Marks content that can animate out as open.</summary>
    internal static void Enter(Node node, bool animate)
    {
        if (animate && node is not TextNode and not Anchor)
        {
            Renderer.SetAttribute(node.Id, "data-state", "open");
        }
    }

    protected override void Apply(Element parent)
    {
        var owner = Scope.Current;
        var anchor = new Anchor();
        Renderer.AppendChild(parent.Id, anchor.Id);

        Scope? branch = null;
        Node? current = null;
        bool? shown = null;

        _ = new Effect(() =>
        {
            var value = condition();
            if (shown == value)
            {
                return;
            }

            shown = value;
            Tracker.Untracked(() =>
            {
                if (current is not null)
                {
                    Leave(current, animateExit);
                    branch!.Dispose();
                    current = null;
                    branch = null;
                }

                var builder = value ? render : fallback;
                if (builder is null)
                {
                    return;
                }

                branch = new Scope(owner);
                current = branch.Run(builder);
                Enter(current, animateExit);
                Renderer.InsertBefore(anchor.Id, current.Id);
            });
        });
    }
}
