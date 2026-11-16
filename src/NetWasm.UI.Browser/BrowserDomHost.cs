using System;
using System.Runtime.InteropServices.JavaScript;
using NetWasm.UI.Dom;

namespace NetWasm.UI.Browser;

/// <summary>
/// <see cref="IDomHost"/> backed by the real browser DOM through <c>dom.mjs</c>.
/// Every flush is exactly one Wasm→JS call carrying the whole op batch as a view of Wasm memory (no copy).
/// Also answers the synchronous reads of <see cref="IDomQueries"/>.
/// </summary>
public sealed class BrowserDomHost : IDomHost, IDomQueries
{
    private const string Module = "nwui.dom";

    [JSImport("applyBatch", Module)]
    private static extern void ApplyBatchJs(ReadOnlySpan<byte> ops);

    [JSImport("subscribe", Module)]
    private static extern JSSubscription SubscribeJs(Action<int, byte[]> dispatch);

    [JSImport("getRect", Module)]
    private static extern double[] GetRectJs(int id);

    [JSImport("activeElementId", Module)]
    private static extern int ActiveElementIdJs();

    [JSImport("contains", Module)]
    private static extern bool ContainsJs(int parentId, int childId);

    [JSImport("viewport", Module)]
    private static extern double[] ViewportJs();

    [JSImport("nodeById", Module)]
    internal static extern JSObject? NodeById(int id);

    private JSSubscription? subscription;

    public Rect GetRect(int id) => Rect.FromArray(GetRectJs(id));

    public int ActiveElementId() => ActiveElementIdJs();

    public bool Contains(int parentId, int childId) => ContainsJs(parentId, childId);

    public Rect Viewport() => Rect.FromArray(ViewportJs());

    public void ApplyBatch(ReadOnlySpan<byte> ops) => ApplyBatchJs(ops);

    /// <summary>Routes DOM events from JS into <see cref="Renderer.Dispatch(int, byte[])"/>.</summary>
    public void Connect()
    {
        subscription ??= SubscribeJs(Renderer.Dispatch);
    }
}
