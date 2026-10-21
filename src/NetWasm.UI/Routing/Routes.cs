using System;
using System.Collections.Generic;
using NetWasm.UI.Dom;
using NetWasm.UI.Reactive;

namespace NetWasm.UI.Routing;

/// <summary>A pattern paired with the page builder it renders.</summary>
public sealed class Route
{
    public Route(string pattern, Func<RouteParams, Node> render)
    {
        Pattern = new RoutePattern(pattern);
        Render = render;
    }

    public RoutePattern Pattern { get; }
    public Func<RouteParams, Node> Render { get; }
}

/// <summary>
/// Renders the first <see cref="Route"/> matching <see cref="Router.Current"/>'s path, rebuilding the
/// page in a fresh scope when the matched route or its parameters change. Works like <see cref="Show"/>
/// with an anchor marking the insertion point.
/// </summary>
public sealed class Routes : Fragment
{
    private readonly IReadOnlyList<Route> routes;
    private readonly Func<Node>? notFound;

    public Routes(IReadOnlyList<Route> routes, Func<Node>? notFound = null)
    {
        this.routes = routes;
        this.notFound = notFound;
    }

    protected override void Apply(Element parent)
    {
        var router = Router.Current ?? throw new InvalidOperationException("Router.Install(history) must be called before rendering Routes.");
        var owner = Scope.Current;
        var anchor = new Anchor();
        Renderer.AppendChild(parent.Id, anchor.Id);

        Scope? pageScope = null;
        Node? page = null;
        var shownIndex = -2; // -1 = not found, -2 = nothing yet
        RouteParams? shownParams = null;

        _ = new Effect(() =>
        {
            var path = router.Path.Value;
            var index = -1;
            var parameters = RouteParams.Empty;
            for (var i = 0; i < routes.Count; i++)
            {
                if (routes[i].Pattern.TryMatch(path, out parameters))
                {
                    index = i;
                    break;
                }
            }

            if (index == shownIndex && (index < 0 || parameters.SameAs(shownParams!)))
            {
                return;
            }

            shownIndex = index;
            shownParams = parameters;

            Tracker.Untracked(() =>
            {
                if (page is not null)
                {
                    page.Remove();
                    pageScope!.Dispose();
                    page = null;
                    pageScope = null;
                }

                pageScope = new Scope(owner);
                page = pageScope.Run(() => index >= 0 ? routes[index].Render(parameters) : notFound?.Invoke());
                if (page is null)
                {
                    pageScope.Dispose();
                    pageScope = null;
                    return;
                }

                Renderer.InsertBefore(anchor.Id, page.Id);
            });
        });
    }
}
