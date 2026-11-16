using System;
using System.Runtime.InteropServices.JavaScript;
using NetWasm.UI.Dom;

namespace NetWasm.UI.Browser;

/// <summary>Browser-only access to the DOM nodes behind NetWasm.UI handles.</summary>
public static class BrowserDom
{
    /// <summary>
    /// The JS object for <paramref name="element"/> (its <c>HTMLElement</c>), for your own <c>[JSImport]</c> functions
    /// (a chart or editor library that needs the element). Pending DOM updates are flushed first, so an element built
    /// moments ago exists. Call from <c>OnMount</c> or an event handler. The handle is only valid while the element is
    /// alive; don't keep it after the component is disposed.
    /// </summary>
    public static JSObject GetHandle(Element element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        Renderer.Flush();
        return BrowserDomHost.NodeById(element.Id)
            ?? throw new InvalidOperationException("The element has no DOM node (it was released, or the app is not running in a browser).");
    }
}
