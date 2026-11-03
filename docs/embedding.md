# Embedding and several apps on one page

By default an app owns the page: `BrowserApp.Start(new App(), "#app")` mounts into `#app` and
the router follows `location`. NetWasm.UI can also run as a widget inside another page, in
several independent instances. `samples/Widgets` shows two instances of one module side by side.

## One DOM host per instance

The browser module `nwui.dom` (`dom.mjs`) is normally bundled into the app. To run several
instances, give each Wasm instance its own host instead:

1. In the `.csproj`, turn off the bundled module and copy `dom.mjs` to the site:

   ```xml
   <PropertyGroup>
     <NetWasmUIAddDomModule>false</NetWasmUIAddDomModule>
   </PropertyGroup>
   <ItemGroup>
     <Content Include="$(NetWasmUIDomModulePath)" Link="nwui\dom.mjs" CopyToPublishDirectory="PreserveNewest" />
   </ItemGroup>
   ```

   `NetWasmUIDomModulePath` points at `dom.mjs` in the `NwUi.Browser` package. The samples
   in this repository link `src/NetWasm.UI.Browser/dom.mjs` directly instead.

2. Create a host per instance and pass it as the `nwui.dom` import module:

   ```js
   import { open } from "/browser/Widgets.mjs";
   import { createDomHost } from "/nwui/dom.mjs";

   async function mount(root, label) {
     const host = createDomHost({ root });
     const app = await open({ modules: { "nwui.dom": host } });
     app.start(label);
     return {
       async close() {
         app.stop();          // a [JSExport] that calls BrowserApp.Stop()
         await app.close();   // releases the Wasm instance
         host.dispose();      // removes the event listeners
       },
     };
   }
   ```

3. In C#, mount into the host's root with an empty selector:

   ```csharp
   [JSExport("start")]
   public static void Start(string label) => BrowserApp.Start(new CounterWidget(label), "");

   [JSExport("stop")]
   public static void Stop() => BrowserApp.Stop();
   ```

Each host has its own node table, templates and event listeners, and delegates events only from
its own root.

## `createDomHost` options

| Option | Default | Meaning |
|---|---|---|
| `root` | `document` | Where events are delegated from and mount selectors are resolved. An empty selector mounts directly into `root`. |
| `basePath` | the path of `<base href>`, or `""` | The path the app is served under. See [Routing](routing.md#hosting-and-base-path). |
| `history` | `window.history` | An object with `currentPath()`, `navigate(path, replace)` and `onPopState(fn)` returning `{ dispose() }`. Pass your own for a widget that must not change the URL. |

Instances on one page share `location`. Give at most one of them the window history, or give
each its own `history` object.

## Things to watch

- Form field ids (`nwf1`, …) are only unique per instance. Set `Field.Id` explicitly when
  instances share a page. See [Forms](forms.md#fields).
- Every instance downloads and compiles the same `.wasm` once (the browser caches it), but each
  has its own memory and state. Share data through the page, for example with a `[JSExport]`
  method or a custom event.
- `BrowserApp.Stop()` unmounts all components and disposes their effects. Call it before
  `app.close()`.
- With a strict Content Security Policy, see [Security](security.md).
