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

    private Router(IHistory history, string basePath)
    {
        this.history = history;
        BasePath = NormalizeBase(basePath);
        path = new Signal<string>(ToApp(history.Path));
        history.Changed += OnHistoryChanged;
    }

    /// <summary>
    /// The path prefix the app is served under, without a trailing slash (<c>/my-app</c>), or empty when it
    /// is served from the root. <see cref="Path"/> and route patterns never include it.
    /// </summary>
    public string BasePath { get; }

    /// <summary>The router installed for this app, or null before <see cref="Install"/>.</summary>
    public static Router? Current { get; private set; }

    /// <summary>Installs a router on top of <paramref name="history"/> and makes it <see cref="Current"/>.</summary>
    /// <param name="history">The history to read and drive.</param>
    /// <param name="basePath">
    /// The path prefix the app is served under (<c>/my-app/</c>); <c>/</c> or empty for the root. Browser
    /// paths outside it are passed through unchanged (they usually end up on the not-found page).
    /// </param>
    public static Router Install(IHistory history, string? basePath = null)
    {
        Current?.Dispose();
        Current = new Router(history, basePath ?? string.Empty);
        return Current;
    }

    /// <summary>Removes the current router. Intended for tests.</summary>
    public static void Reset()
    {
        Current?.Dispose();
        Current = null;
    }

    /// <summary>The current app path (without <see cref="BasePath"/>) including query and hash. Reading it inside an effect tracks it.</summary>
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

        history.Navigate(Href(to), replace);
        path.Value = to;
    }

    /// <summary>The browser URL for an app path: <see cref="BasePath"/> + <paramref name="appPath"/>. Other URLs are returned unchanged.</summary>
    public string Href(string appPath)
    {
        if (BasePath.Length == 0 || appPath.Length == 0 || appPath[0] != '/' || (appPath.Length > 1 && appPath[1] == '/'))
        {
            return appPath;
        }

        return appPath == "/" ? BasePath + "/" : BasePath + appPath;
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

    private void OnHistoryChanged(string newPath) => path.Value = ToApp(newPath);

    private string ToApp(string browserPath)
    {
        var b = BasePath;
        if (b.Length == 0 || !browserPath.StartsWith(b, StringComparison.Ordinal))
        {
            return browserPath;
        }

        if (browserPath.Length == b.Length)
        {
            return "/";
        }

        var next = browserPath[b.Length];
        if (next == '/')
        {
            return browserPath.Substring(b.Length);
        }

        return next == '?' || next == '#' ? "/" + browserPath.Substring(b.Length) : browserPath;
    }

    private static string NormalizeBase(string basePath)
    {
        var start = 0;
        var end = basePath.Length;
        while (start < end && basePath[start] == '/')
        {
            start++;
        }

        while (end > start && basePath[end - 1] == '/')
        {
            end--;
        }

        return end == start ? string.Empty : "/" + basePath.Substring(start, end - start);
    }

    private void Dispose() => history.Changed -= OnHistoryChanged;
}
