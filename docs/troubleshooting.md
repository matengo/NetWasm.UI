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
- `System.Linq`, reflection and `async` are not available in `netwasm0.1`. Write plain loops.
- `[JSImport]` methods must be `static extern`. A host function used as `JSSubscription` must
  return an object with a `dispose()` method.
- The DOM decoder (`dom.mjs`) must be declared by the **app** project
  (`<NetWasmJavaScriptModule Include="…/dom.mjs" Module="netwasm.ui.dom" />`). It is then bundled
  into `netwasm.page.mjs`.

## The Wasm got much bigger

Look for string interpolation with non-string values, interface calls such as `IFormattable`,
new generic instantiations, and `Uri.EscapeDataString`. See
[Keeping the Wasm small](deployment.md#keeping-the-wasm-small).

## Known limitations

- NetWasm 0.10 (`netwasm0.1`) has no `System.Linq`, reflection or `async`.
- `Scheduler`, `Renderer`, the tracker and `Router.Current` are static: one app per Wasm module
  instance.
- An exception thrown in an event handler or effect is not caught by the framework. There is no
  error boundary yet.
- No base-path or hash routing, no nested/layout routes, no SSR or hydration.
- No hot reload: the dev server rebuilds (15–20 s) and reloads the page, and signal state is lost.
- `.nwui` editor support is syntax highlighting only (VS Code). Control flow is only
  `@if`/`@foreach`, and slots take no parameters (no `Slot<T>`).
- Not on NuGet yet; apps reference the projects in this repository.
