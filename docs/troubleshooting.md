# Troubleshooting

## The page is blank or a module fails to load

- **Opened from `file://`.** ES modules and Wasm need HTTP. Use the dev server or any static
  server on `http://localhost`.
- **Wrong MIME type.** `.mjs` must be served as `text/javascript` and `.wasm` as
  `application/wasm`. Browsers cache a wrong module MIME type aggressively, so hard-reload
  (Ctrl+Shift+R) after fixing the server.
- **Deep link returns 404 or loads `/todos/browser/App.mjs`.** The server needs an SPA fallback
  to `index.html`, and `index.html`/`app.mjs` must use absolute URLs (`/app.mjs`,
  `/browser/App.mjs`). See [Publishing](deployment.md#hosting).
- **A deep link returns 404 with `dotnet run`.** The NetWasm SDK's own server has no SPA
  fallback. Use `dotnet nwui-dev`.
- **A path containing a dot returns 404.** The dev server only falls back to `index.html` for
  paths without a file extension (`/users/john.doe` looks like a file). Avoid dots in the last
  segment of routes, or configure your production server's fallback differently.
- **A Content Security Policy error in the console.** See [Security](security.md#content-security-policy):
  NetWasm needs `'wasm-unsafe-eval'` and `blob:` in `script-src`.
- **An old browser.** NetWasm needs Chrome/Edge 137, Firefox 131 or Safari 18.4. See
  [Compatibility](compatibility.md#browsers).
- **Nothing mounts.** `BrowserApp.Start(root, "#app")` needs an element matching the selector in
  `index.html`.

## The browser shows old code after a change

The dev server sends `Cache-Control: no-cache`, so a normal reload is enough. Other servers may
cache `browser/*.wasm` because NetWasm file names do not change between builds; see
[Caching](deployment.md#caching).

## A change does not show up

- `Build()` runs once. Reading `count.Value` directly in `Build()` (or in `OnInit`, or reading a
  `Prop<T>` there) is a snapshot. Read it inside a `Func`: `Text(() => count.Value)`,
  `Class(() => …)`, `When(() => …)`, or `@count` in a template.
- Plain component properties are read once. Use `Prop<T>` (`@prop` in templates) or pass a
  `Signal<T>` for values that change. See [Props](components.md#props).
- A list was mutated in place. Assign a new list or call `signal.Touch()`.
- `Use(context)` was called inside a reactive lambda. Call it while building.

## Build errors

- `NWUI001`–`NWUI003` come from the template generator and point at the `.nwui` file; see
  [Templates](templates.md#errors).
- Reflection is not available in `netwasm0.1`. For `System.Text.Json`, use a source-generated
  `JsonSerializerContext`. Use the `NetWasm.*` packages (`NetWasm.System.Net.Http`,
  `NetWasm.System.Text.Json`) rather than the Microsoft ones; the Microsoft packages fail with
  `NU1202`.
- `NWSDK078`: a page app needs a `Main` method, even an empty one, next to its `[JSExport]`
  start method.
- `[JSImport]` methods must be `static extern`. A host function used as `JSSubscription` must
  return an object with a `dispose()` method.
- `nwui.dom` is missing at runtime. The `NwUi.Browser` package adds `dom.mjs` to apps
  with `OutputType` `JsModule` or `Exe`. With `NetWasmUIAddDomModule=false` you must pass your
  own host to `open()`; see [Embedding](embedding.md).

## The build is very slow

`wasm-opt` at `Oz` can take minutes for an app that uses `HttpClient` or `System.Text.Json`. Use
`-p:NetWasmOptimization=O1` for Debug builds, as `samples/Fetch` does, and keep `Oz` for
releases. The dev server uses `O1` unless you pass `--release`.

## The Wasm got much bigger

Look for string interpolation with non-string values, interface calls such as `IFormattable`,
new generic instantiations, and `Uri.EscapeDataString`. See
[Keeping the Wasm small](deployment.md#keeping-the-wasm-small).

## Known limitations

- NetWasm 0.10 (`netwasm0.1`) has no reflection. The core library does not use `System.Linq`;
  apps may use the NetWasm LINQ package, at a size cost.
- `Scheduler`, `Renderer`, the tracker and `Router.Current` are static: one app per Wasm module
  instance.
- An exception thrown outside the framework (your own `JSImport` callbacks, timers) is not
  caught by an `ErrorBoundary`. See [Error handling](error-handling.md).
- No hash routing, no SSR or hydration.
- No hot reload: the dev server rebuilds (5–20 s) and reloads the page, and signal state is lost.
- `.nwui` editor support is syntax highlighting only (VS Code). Control flow is only
  `@if`/`@foreach`, and slots take no parameters (no `Slot<T>`).
