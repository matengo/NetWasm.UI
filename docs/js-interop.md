# JavaScript interop

NetWasm.UI renders through its own interop layer (`dom.mjs`), so most apps never call JavaScript
themselves. You need interop for:

- **JS libraries** that own a piece of the DOM: charts, maps, editors.
- **Browser APIs** the framework does not wrap: `localStorage`, clipboard, geolocation, canvas,
  `IntersectionObserver`.
- **Embedding**: letting an existing JavaScript page call into your app.

Interop is NetWasm's `[JSImport]`/`[JSExport]`. This page covers what matters in a NetWasm.UI app.
For every supported type and option, see NetWasm's
[JavaScript interop](https://github.com/zion-sati/NetWasm/blob/main/docs/javascript-interop.md)
guide. `samples/Interop` is the complete, tested example used below.

## Calling JavaScript

Write the JS functions in an ES module:

```js
// meter.mjs
export function setValue(meter, value) { meter.setValue(value); }
```

Register it in the project file under a module name:

```xml
<ItemGroup>
  <NetWasmJavaScriptModule Include="meter.mjs" Module="interop.meter" />
</ItemGroup>
```

Declare the functions in C#:

```csharp
using System.Runtime.InteropServices.JavaScript;

internal static class MeterJs
{
    private const string Module = "interop.meter";

    [JSImport("setValue", Module)]
    public static extern void SetValue(JSObject meter, int value);
}
```

- Imports must be `static extern` (or `static partial` with an access modifier, otherwise
  `NWJS001`) and must name their module.
- The module is bundled into the app's generated `.mjs`. A missing file is `NWSDK093`.
- Page-host modules run in the page, so they can use `document` and `window`.
- Keep the JS module thin. Pass strings and numbers, not big object graphs. A sync import with
  only primitive parameters and result compiles to a direct Wasm call.
- An error thrown in JS arrives in C# as a `JSException`.

## Promises

An import that returns `Task` or `Task<T>` maps a JS `Promise` (use a plain `[JSImport]`, not
`JSImportPromise`):

```js
export async function load() {
  lib = await import(new URL("vendor/meter-lib.mjs", document.baseURI).href);
}
```

```csharp
[JSImport("load", Module)]
public static extern Task Load();
```

To show the result in the UI, wrap the call in a [`Resource`](async.md):

```csharp
var position = Resource.Create(ct => GeoJs.GetPositionAsync().WaitAsync(ct));
```

JS promises cannot be cancelled. `WaitAsync(ct)` stops waiting when the resource is refetched
or disposed, but the JS work keeps running.

## Wrapping a JS library in a component

A library that owns DOM needs an element to render into, and it must be torn down with the
component. `samples/Interop/Meter.cs` shows the pattern:

```csharp
public sealed class Meter : Component
{
    private static int instances;

    public Signal<int> Level { get; set; } = null!;

    protected override Node Build()
    {
        var id = "meter-" + (++instances).ToString();
        var level = Level;
        JSObject? meter = null;
        JSSubscription? changes = null;

        OnMount(() =>                                        // 1. the element is in the document
        {
            meter = MeterJs.Create("#" + id, 10);
            MeterJs.SetValue(meter, level.Peek());
            changes = MeterJs.OnChange(meter, v => Scheduler.Batch(() => level.Value = v));
        });

        new Effect(() =>                                     // 2. signal -> library
        {
            var value = level.Value;
            if (meter is not null)
            {
                MeterJs.SetValue(meter, value);
            }
        });

        OnCleanup(() =>                                      // 3. component removed
        {
            changes?.Dispose();
            if (meter is not null)
            {
                MeterJs.Destroy(meter);
                meter.Dispose();
            }
        });

        return Div(Id(id), Class("meter"));                  // empty: the library owns it
    }
}
```

1. **Create in `OnMount`.** While `Build` runs, the element does not exist in the browser yet.
   `OnMount` runs after the DOM ops were applied. Elements have no JS handle in C#, so give the
   container a unique `id` and look it up in JS with `document.querySelector`.
2. **Push changes with an `Effect`.** It reads the signal, so it re-runs on every change. Its
   first run happens during `Build`, before the library exists, which is why it checks for
   `null`. Use `Peek()` in `OnMount` to read without subscribing.
3. **Tear down in `OnCleanup`.** It runs when the component, `When` branch, `Each` row or page is
   removed. Call the library's destroy function, then dispose the `JSObject` and every
   `JSSubscription`. Otherwise the JS object and the C# callback stay alive.

Do not render framework children inside the library's container: both sides would change the
same DOM.

### JSObject

An import that returns `JSObject` hands C# a reference to a JS object. Pass it back to other
imports. It owns a handle, so dispose it when you are done.

## Callbacks from JavaScript

A JS function that takes a callback and returns an object with `dispose()` maps to an import
with exactly one delegate parameter and a `JSSubscription` result. Any other shape is `NW1003`.

```js
export function onChange(meter, fn) {
  meter.onChange = fn;
  return { dispose() { meter.onChange = undefined; } };
}
```

```csharp
[JSImport("onChange", Module)]
public static extern JSSubscription OnChange(JSObject meter, Action<int> handler);
```

- Disposing the subscription calls `dispose()` and releases the C# delegate.
- The callback runs outside the framework's event handlers. Wrap signal writes in
  `Scheduler.Batch` so that effects and the DOM update run once (see
  [Batching](reactivity.md#batching)).
- An exception in the callback is **not** caught by an `ErrorBoundary`. Catch it in the callback,
  for example to set an error signal. See [Error handling](error-handling.md).

## Calling C# from JavaScript

`[JSExport]` makes a static method callable from the page. The generated module's `open()`
returns an object with the exports:

```csharp
public static partial class Program
{
    public static void Main()
    {
    }

    [JSExport("start")]
    public static async Task Start()
    {
        await MeterJs.Load();
        BrowserApp.Start(new InteropPage(), "#app");
    }
}
```

```js
import { open } from "/browser/Interop.mjs";
const app = await open();
await app.start();
```

- A page app needs a `Main` method, even an empty one (`NWSDK078`).
- An async export returns a `Promise` to JS.
- To update the UI from an export, write signals inside `Scheduler.Batch`.
- See [Embedding](embedding.md) for start/stop exports and several apps on one page.

## Pitfalls

| Symptom | Cause |
|---|---|
| `NW1003` | A callback import needs exactly one delegate parameter and a `JSSubscription` result. |
| `NWJS001` | A `static partial` import without an access modifier. Use `static extern`. |
| `NWSDK093` | The `NetWasmJavaScriptModule` file does not exist. |
| `NWSDK078` | No `Main` method next to the `[JSExport]` start method. |
| `JSException` | The JS function threw. The message is the JS error's message. |
| `querySelector` returns `null` | Called while building. Move the call to `OnMount`. |
| The library keeps running after navigation | Missing `OnCleanup`, or `JSObject`/`JSSubscription` not disposed. |
| The UI updates several times per callback | Signal writes outside `Scheduler.Batch`. |
| Blocked by Content Security Policy | NetWasm needs `script-src 'self' blob: 'wasm-unsafe-eval'`. See [Security](security.md). |

Every import adds code to the Wasm module. In a size-sensitive app, keep imports few and simple.
