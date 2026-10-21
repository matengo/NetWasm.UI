using System;
using NetWasm.UI.Reactive;

namespace NetWasm.UI.Routing;

/// <summary>
/// Client-side router: exposes the current path as a signal and drives the <see cref="IHistory"/>.
/// Create one per app with <see cref="Install"/>; <see cref="Routes"/> and <see cref="Html.Link"/> use <see cref="Current"/>.
/// </summary>
public sealed class Router
{
    private readonly IHistory history;
    private readonly Signal<string> path;

    private Router(IHistory history)
    {
        this.history = history;
        path = new Signal<string>(history.Path);
        history.Changed += OnHistoryChanged;
    }

    /// <summary>The router installed for this app, or null before <see cref="Install"/>.</summary>
    public static Router? Current { get; private set; }

    /// <summary>Installs a router on top of <paramref name="history"/> and makes it <see cref="Current"/>.</summary>
    public static Router Install(IHistory history)
    {
        Current?.Dispose();
        Current = new Router(history);
        return Current;
    }

    /// <summary>Removes the current router. Intended for tests.</summary>
    public static void Reset()
    {
        Current?.Dispose();
        Current = null;
    }

    /// <summary>The current path including query and hash. Reading it inside an effect tracks it.</summary>
    public IReadOnlySignal<string> Path => path;

    /// <summary>Path without query and hash.</summary>
    public string PathName
    {
        get
        {
            var p = path.Value;
            var end = p.Length;
            var q = p.IndexOf('?');
            var h = p.IndexOf('#');
            if (q >= 0 && q < end) end = q;
            if (h >= 0 && h < end) end = h;
            return p.Substring(0, end);
        }
    }

    /// <summary>Navigates to <paramref name="to"/>, pushing a history entry unless <paramref name="replace"/>.</summary>
    public void Navigate(string to, bool replace = false)
    {
        if (to == path.Value)
        {
            return;
        }

        history.Navigate(to, replace);
        path.Value = to;
    }

    /// <summary>True when the current path is exactly <paramref name="href"/> or (unless <paramref name="exact"/>) starts with it.</summary>
    public bool IsActive(string href, bool exact = false)
    {
        var current = PathName;
        if (string.Equals(current, href, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (exact || href == "/")
        {
            return false;
        }

        return current.StartsWith(href, StringComparison.OrdinalIgnoreCase) && current.Length > href.Length && current[href.Length] == '/';
    }

    private void OnHistoryChanged(string newPath) => path.Value = newPath;

    private void Dispose() => history.Changed -= OnHistoryChanged;
}
