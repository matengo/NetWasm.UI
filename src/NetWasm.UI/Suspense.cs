using System;
using NetWasm.UI.Dom;
using NetWasm.UI.Reactive;

namespace NetWasm.UI;

/// <summary>
/// Shows <c>fallback</c> until every <see cref="Resource{T}"/> created inside <c>content</c> has finished
/// its first load. The content is built immediately (so its resources start loading) but is only
/// inserted into the document once nothing is pending. Later reloads keep the content on screen; use
/// <see cref="Resource{T}.Loading"/> to show inline progress. Wrap it in an <see cref="ErrorBoundary"/>
/// to handle failed loads (reading <see cref="Resource{T}.Value"/> of a failed resource throws).
/// <para>
/// <c>OnMount</c> callbacks inside the content run when it is built, while it is still detached;
/// for focus or measurement, run them from an effect on the resource instead.
/// </para>
/// </summary>
public sealed class Suspense : Fragment
{
    private readonly Func<Node> content;
    private readonly Func<Node> fallback;

    public Suspense(Func<Node> content, Func<Node> fallback)
    {
        this.content = content;
        this.fallback = fallback;
    }

    protected override void Apply(Element parent)
    {
        var owner = Scope.Current;
        var anchor = new Anchor();
        Renderer.AppendChild(parent.Id, anchor.Id);

        var registry = new SuspenseRegistry();
        var branch = new Scope(owner);
        branch.SetContext(SuspenseRegistry.Key, registry);
        var body = branch.Run(content);

        Scope? fallbackScope = null;
        Node? fallbackNode = null;
        var revealed = false;

        _ = new Effect(() =>
        {
            if (revealed)
            {
                return;
            }

            var pending = registry.Pending > 0;
            Tracker.Untracked(() =>
            {
                if (pending)
                {
                    if (fallbackNode is null)
                    {
                        fallbackScope = new Scope(owner);
                        fallbackNode = fallbackScope.Run(fallback);
                        Renderer.InsertBefore(anchor.Id, fallbackNode.Id);
                    }

                    return;
                }

                revealed = true;
                if (fallbackNode is not null)
                {
                    fallbackNode.Remove();
                    fallbackScope!.Dispose();
                    fallbackNode = null;
                    fallbackScope = null;
                }

                Renderer.InsertBefore(anchor.Id, body.Id);
            });
        });
    }
}
