using System;
using System.ComponentModel;
using System.Runtime.InteropServices.JavaScript;
using NetWasm.UI.Reactive;

namespace NetWasm.UI.Browser;

/// <summary>
/// Keeps <c>int</c>, <c>bool</c> and <c>string</c> signal values when the dev server reloads the page.
/// </summary>
/// <remarks>
/// Debug builds call <see cref="Install"/> from a generated module initializer (set
/// <c>NetWasmUIDevState</c> to <c>false</c> to opt out, or <c>true</c> to force it on). Release builds never
/// call it, so it adds nothing to their Wasm. Outside the dev server it does nothing.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class DevReload
{
    private static bool installed;
    private static JSSubscription? subscription;

    /// <summary>Connects signal state to the dev server's reload client, once.</summary>
    public static void Install()
    {
        if (installed)
        {
            return;
        }

        installed = true;

        // "" without the dev server's reload client; otherwise "1" and the state saved before the reload.
        var state = DevStateJs();
        if (state.Length > 0)
        {
            DevState.Enable(state.Substring(1));
            subscription = DevConnectJs(Save);
        }
    }

    private static void Save(int _) => DevPutJs(DevState.Serialize());

    [JSImport("devState", "nwui.dom")]
    private static extern string DevStateJs();

    [JSImport("devConnect", "nwui.dom")]
    private static extern JSSubscription DevConnectJs(Action<int> save);

    [JSImport("devPut", "nwui.dom")]
    private static extern void DevPutJs(string state);
}
