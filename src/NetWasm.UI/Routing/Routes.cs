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
/// <remarks>
/// Routes nest: a page matched by a prefix pattern such as <c>/settings/*</c> can render its own
/// <see cref="Routes"/>, whose patterns are relative to the prefix (<c>/profile</c> matches
/// <c>/settings/profile</c>). The outer page (the layout) stays mounted while only the nested part changes.
/// </remarks>
public sealed class Routes : Fragment
{
    private static readonly object RemainderKey = new();

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
        IReadOnlySignal<string>? outer = null;
        if (owner is not null && owner.TryGetContext(RemainderKey, out var found))
        {
            outer = (IReadOnlySignal<string>)found!;
        }

        var anchor = new Anchor();
        Renderer.AppendChild(parent.Id, anchor.Id);

        Scope? pageScope = null;
        Node? page = null;
        Signal<string>? remainder = null;
        var shownIndex = -2; // -1 = not found, -2 = nothing yet
        RouteParams? shownParams = null;

        _ = new Effect(() =>
        {
            var path = outer is null ? router.Path.Value : outer.Value;
            var index = -1;
            var parameters = RouteParams.Empty;
            var rest = string.Empty;
            for (var i = 0; i < routes.Count; i++)
            {
                if (routes[i].Pattern.TryMatch(path, out parameters, out rest))
                {
                    index = i;
                    break;
                }
            }

            if (index == shownIndex && (index < 0 || parameters.SameAs(shownParams!)))
            {
                if (remainder is not null)
                {
                    remainder.Value = rest;
                }

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
                remainder = null;
                if (index >= 0 && routes[index].Pattern.IsPrefix)
                {
                    remainder = new Signal<string>(rest);
                    pageScope.SetContext(RemainderKey, remainder);
                }

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
