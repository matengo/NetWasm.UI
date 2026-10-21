using System;
using System.Runtime.InteropServices.JavaScript;
using NetWasm.UI.Dom;

namespace NetWasm.UI.Browser;

/// <summary>
/// <see cref="IDomHost"/> backed by the real browser DOM through <c>dom.mjs</c>.
/// Every flush is exactly one Wasm→JS call carrying the whole op batch.
/// </summary>
public sealed class BrowserDomHost : IDomHost
{
    private const string Module = "netwasm.ui.dom";

    [JSImport("applyBatch", Module)]
    private static extern void ApplyBatchJs(byte[] ops);

    [JSImport("subscribe", Module)]
    private static extern JSSubscription SubscribeJs(Action<int, string> dispatch);

    private JSSubscription? subscription;

    public void ApplyBatch(byte[] ops) => ApplyBatchJs(ops);

    /// <summary>Routes DOM events from JS into <see cref="Renderer.Dispatch"/>.</summary>
    public void Connect()
    {
        subscription ??= SubscribeJs(Renderer.Dispatch);
    }
}
