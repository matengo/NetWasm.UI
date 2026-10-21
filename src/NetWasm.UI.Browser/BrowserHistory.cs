using System;
using System.Runtime.InteropServices.JavaScript;
using NetWasm.UI.Routing;

namespace NetWasm.UI.Browser;

/// <summary><see cref="IHistory"/> over the browser History API via <c>dom.mjs</c>.</summary>
public sealed class BrowserHistory : IHistory
{
    private const string Module = "netwasm.ui.dom";

    [JSImport("currentPath", Module)]
    private static extern string CurrentPathJs();

    [JSImport("navigate", Module)]
    private static extern void NavigateJs(string path, bool replace);

    [JSImport("onPopState", Module)]
    private static extern JSSubscription OnPopStateJs(Action<string> handler);

    private static BrowserHistory? instance;
    private JSSubscription? subscription;

    public BrowserHistory()
    {
        instance = this;
        subscription ??= OnPopStateJs(OnPopState);
    }

    public string Path => CurrentPathJs();

    public event Action<string>? Changed
    {
        add => changed.Add(value!);
        remove => changed.Remove(value!);
    }

    // A list instead of a field-like event: avoids pulling multicast delegate code into the Wasm module.
    private readonly System.Collections.Generic.List<Action<string>> changed = new();

    private void Raise(string path)
    {
        foreach (var handler in changed.ToArray())
        {
            handler(path);
        }
    }

    public void Navigate(string path, bool replace) => NavigateJs(path, replace);

    private static void OnPopState(string path) => instance?.Raise(path);
}
