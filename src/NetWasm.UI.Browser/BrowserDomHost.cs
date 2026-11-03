using System;
using System.Runtime.InteropServices.JavaScript;
using NetWasm.UI.Dom;

namespace NetWasm.UI.Browser;

/// <summary>
/// <see cref="IDomHost"/> backed by the real browser DOM through <c>dom.mjs</c>.
/// Every flush is exactly one Wasm→JS call carrying the whole op batch as a view of Wasm memory (no copy).
/// </summary>
public sealed class BrowserDomHost : IDomHost
{
    private const string Module = "nwui.dom";

    [JSImport("applyBatch", Module)]
    private static extern void ApplyBatchJs(ReadOnlySpan<byte> ops);

    [JSImport("subscribe", Module)]
    private static extern JSSubscription SubscribeJs(Action<int, byte[]> dispatch);

    private JSSubscription? subscription;

    public void ApplyBatch(ReadOnlySpan<byte> ops) => ApplyBatchJs(ops);

    /// <summary>Routes DOM events from JS into <see cref="Renderer.Dispatch(int, byte[])"/>.</summary>
    public void Connect()
    {
        subscription ??= SubscribeJs(Renderer.Dispatch);
    }
}
