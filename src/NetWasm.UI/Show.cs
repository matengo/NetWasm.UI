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

    public Show(Func<bool> condition, Func<Node> render, Func<Node>? fallback = null)
    {
        this.condition = condition;
        this.render = render;
        this.fallback = fallback;
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
                    current.Remove();
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
                Renderer.InsertBefore(anchor.Id, current.Id);
            });
        });
    }
}
