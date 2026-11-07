# Changelog

All notable changes to NetWasm.UI. The project follows [Semantic Versioning](https://semver.org);
in 0.x, minor releases may contain breaking changes, and they are listed here with what to do.
See [Compatibility and versioning](docs/compatibility.md).

## [Unreleased]

## [0.5.0] - 2026-11-07

### Added

- [JavaScript interop](docs/js-interop.md) guide: `[JSImport]`, Promises, `JSObject`, callbacks
  with `JSSubscription`, `[JSExport]`, and wrapping a JS library in a component.
- `samples/Interop`: a JS library driven by a signal in both directions, with E2E tests.
- Dev reloads keep state. After a rebuild, `nwui-dev` restores `int`, `bool` and `string`
  signals, form fields, focus and scroll; F5 starts clean. Signals are matched by creation order
  and initial value, so a signal whose initial value or position changed starts fresh. Lists and
  objects are not kept. Active only in `Debug` builds (a generated module initializer calls
  `DevReload.Install()`); `<NetWasmUIDevState>true|false</NetWasmUIDevState>` overrides. Release
  builds do not include the code (+0.1 KB Brotli for the hook in `Signal<T>`).

### Changed

- NetWasm.Sdk 0.10.7 → 0.11.1 (template and repository); `NetWasm.System.*` packages in the Fetch
  sample → 0.11.0. **Breaking:** set `"NetWasm.Sdk": "0.11.1"` under `msbuild-sdks` in your
  `global.json`, and any `NetWasm.System.*` packages to 0.11.0. The Counter sample's Wasm grows
  from 101 to 108 KB Brotli with the same code
  (no SDK default changed; the growth is in the runtime/compiler), so `size-budget.json` is
  raised to 111 KB Wasm / 160 KB total.
- The dev server (`nwui-dev`) now builds `Debug` with `NetWasmOptimization=O0` by default.
  NetWasm 0.11 recompiles only changed methods there, and watch mode keeps the build servers
  warm: save → reloaded page drops from ~8 s to ~3.5 s on the Counter sample. `--release` now
  means `-c Release -O Oz`.

## [0.4.0] - 2026-11-04

### Added

- `SetInterval(tick, milliseconds)` in `Html`: a repeating timer that stops when its component,
  branch or effect run is torn down. Each tick is batched, and errors go to the nearest
  `ErrorBoundary`. The Counter sample shows the seconds spent on the page.
- `Services.AddLazy<TService, TImplementation>()` and `AddLazy<T>()`: register a class whose
  constructor parameters are injected. The source generator rewrites each call at compile time
  (a C# interceptor) into `AddLazy<TService>(() => new TImplementation(Inject<…>(), …))`, with no
  reflection. Types it cannot construct are build error `NWUI010`. The NwUi package sets
  `InterceptorsNamespaces`. The Fetch sample now registers its `Api` this way and uses `@inject`.
  See [Services](docs/services.md#auto-wired-services).

## [0.3.0] - 2026-11-04

### Added

- Constructor injection for component tags: in a `.nwui` template, `<Forecast />` compiles to
  `new Forecast(Inject<IWeatherApi>())` when the component's only constructor takes parameters
  (primary constructors and code-behind partial classes included).

## [0.2.0] - 2026-11-04

### Added

- **Services and DI:** `Services` (`Add<T>`, `AddLazy<T>`), `Inject<T>()`/`TryInject<T>()`,
  `Provide(services)`/`Provide(services, render)`, `App.Mount(root, selector, services)` and
  `BrowserApp.Start(root, services)`. Lookups walk the scope tree like `Context<T>`, with no
  reflection. Lazy services are created once per providing scope and disposed with it.
  See [Services and DI](docs/services.md).
- **Templates:** `@inject Type Name` declares a field filled from `Inject<T>()` before `OnInit`.

## [0.1.0] - 2026-11-03

The first public release: NuGet packages, a `dotnet new` template and the features needed for
real applications. It requires NetWasm.Sdk 0.10.7 and the .NET SDK 10.0.3xx.

### Added

- **Packages:**
  - `NwUi` and `NwUi.Browser` on NuGet. The packages set up the source generator,
    the `*.nwui` files and the `nwui.dom` browser module automatically.
  - Opt-outs: `NetWasmUIEnableTemplates` and `NetWasmUIAddDomModule`.
  - `NetWasmUIDomModulePath`, for apps that create their own DOM hosts.
- **Template:** `NwUi.Templates` (`dotnet new netwasm-ui`): an app with a router, a C#
  component, `.nwui` pages and the dev server as a local tool.
- **Dev server:** `NwUi.DevServer` (`dotnet nwui-dev`), the dev server as a .NET tool.
- **Async data:** `Resource<T>` (keyed reloads, cancellation, `Latest`, `Refetch`, `Mutate`)
  and `Suspense` / `<Suspense>`. `samples/Fetch` shows `HttpClient` and `System.Text.Json`.
- **Forms:**
  - `Form`, `Field<T>` and `Validators`.
  - `@bind` for fields sets the input's `id`, `aria-invalid` and `aria-describedby`.
  - `FieldError`, server errors with `SetError`, and async submit with `Submitting`.
  - Focus moves to the first invalid input.
- **Routing:**
  - Nested routes and layouts with prefix patterns (`/settings/*`).
  - Base path support from `<base href>`, `BrowserApp.Start(..., basePath)` or
    `createDomHost({ basePath })`, and `Router.Href`.
  - A new page scrolls to the top.
- **Embedding:**
  - `createDomHost({ root, basePath, history })` gives every Wasm instance its own DOM host, so
    several apps can run on one page (`samples/Widgets`).
  - `BrowserApp.Stop()` / `App.Unmount()`.
- **Security:**
  - `dom.mjs` creates a Trusted Types policy named `nwui` for template HTML.
  - A tested strict Content Security Policy in [docs/security.md](docs/security.md).
- **Quality:**
  - Public API baselines (`PublicAPI.*.txt`).
  - E2E tests in Chromium, Firefox and WebKit.
  - A size budget in CI (`size-budget.json`, `tools/size-report`).
  - A packaging and template smoke test in CI.
  - A tag-driven release workflow.
  - Benchmark runner: `--apps` and `--cases` select a subset of apps and cases for quick runs.
- **VS Code extension:** prepared for the Marketplace (icon, metadata, `npm run package`).

### Changed

- NetWasm.Sdk 0.10.0 → 0.10.7. The DOM batch is passed to JavaScript without copying.
- `Computed` is push-pull: a change marks it and everything below it stale, and it recomputes on
  read, only if a dependency actually changed. A diamond recomputes its join once per change
  (benchmark `diamond`: 7.8 → 2.6 ms), and a `Computed` nobody reads is not recomputed. An effect
  behind a `Computed` whose value didn't change is now queued and then skipped instead of never
  being queued; it still does not run.
- Short ASCII strings in a render batch are encoded and decoded without `Encoding.UTF8` /
  `TextDecoder`.
- The `.nwui` generator adds `using System.Threading.Tasks;` and `using NetWasm.UI.Forms;` to
  every generated file.

### Breaking changes

These apply if you used the projects in this repository before 0.1.0:

- **Import module renamed.** The browser module is now `nwui.dom`; it was `netwasm.ui.dom`.
  With the package, remove your own `<NetWasmJavaScriptModule … dom.mjs>` item; the package
  adds it.
- **`IDomHost.ApplyBatch`** takes a `ReadOnlySpan<byte>`; it took a `byte[]`.
- **Internal APIs.** `BatchWriter` and the low-level operations on `Renderer` are now internal.
  Use components and the `Html` API.
- **`Router.Install`** has a new parameter: `Router.Install(history, basePath = null)`.
- **`BrowserApp.Start`** has a new parameter: `BrowserApp.Start(root, selector, basePath = null)`.
  Code that compiled against the old signatures needs to be recompiled.
- **Dev server.** Run it as `dotnet nwui-dev` (from the package) or
  `dotnet run --project tools/DevServer` (in this repository). With `--no-watch` it no longer
  injects the live-reload script.

### Before 0.1.0

#### Core

- Signals (`Signal<T>`, `Computed<T>`, `Effect`, `Selector<TKey>`), a batching `Scheduler` and
  `Scope` ownership.
- Build-once components and the `Html` builder API: elements, static and reactive attributes,
  properties, events, `Bind`, `Spread`, `When`, keyed `Each` with a minimal-move diff.
- Binary render protocol: one `applyBatch` call per update, decoded by `dom.mjs`.
- Structured event payloads (`DomEvent`: key, value, checked, modifiers, button, coordinates).
- Client-side router: `Routes`, `Route` patterns with `{id}` and `{*rest}`, `Link`, `NavLink`,
  `MemoryHistory` for tests.
- Context and stores (`Context<T>`, `Provide`, `Use`) and lifecycle hooks (`OnMount`,
  `OnCleanup`).
- Error handling: `ErrorBoundary` with a fallback and `Reset()`, and a global `App.OnError`
  hook. Errors from builds, effects, event handlers and `OnMount` go to the nearest boundary.
  Unhandled errors are rethrown after the DOM update has been applied.

#### Templates

- `.nwui` templates compiled by a Roslyn source generator: `@expr`, attributes, events,
  `@bind`, `@ref`, `@if`/`else`, keyed `@foreach`, components, `@code`, `#line` mapping for
  C# errors.
- Child content and named slots (`Slot`, `@render`, `<template slot="…">`).
- `<ErrorBoundary>` with `<template slot="Fallback" context="error">`.
- Reactive props: `@prop` and `Prop<T>`.
- VS Code extension with syntax highlighting (`tools/vscode-nwui`).

#### Tooling

- Kestrel dev server (replacing a Python server): Brotli/gzip, correct MIME types, SPA fallback,
  live reload over WebSocket, build error overlay, content-hash change detection, `O1` dev
  builds, `no-cache` with 304s.
- Playwright E2E tests and GitHub Actions CI with a size report.
- Benchmark against SolidJS, React and Blazor WebAssembly (`benchmarks/`).

#### Size

- `Counter.wasm` reduced from 645 KB to 463 KB (102 → 77 KB Brotli): no interpolation with
  number holes, no `IFormattable`, a non-generic keyed-list diff, own URL encoding, no multicast
  delegates.

#### Performance (fixes found by the benchmark)

First run (Oz, script + layout) before the fixes: select row 40.8 ms, clear 119.9 ms, replace
213.8 ms, create 10,000 rows 2369.6 ms, reactive `broad` 3509.2 ms. After: 11.3, 15.8, 106.1,
1050.4 and 65.7 ms.

- Signals kept dependents in a `List` (`Contains` on subscribe, `Remove` on re-track), so many
  readers of one signal were O(n²). Links are now index-based (O(1)), and `Selector<TKey>` was
  added for selection state.
- `dom.mjs` scanned every listener for each released node; it now keeps a per-node handler list.
- `Each` row scopes were registered with (and never removed from) the owner scope.
- Reactive attributes emitted an op on every re-run; they now skip unchanged values.

#### Performance (template cloning and event delegation)

- `Template`: static HTML with holes (`<tag $>`, `<!--$-->`, `<!--@-->`) that the browser parses
  once and clones. New ops `DefineTemplate` and `CloneTemplate`; appending to a comment inserts
  before it, so any content can go into an insertion point.
- The `.nwui` generator turns every native element subtree into a static template; only bound
  attributes, handlers, `@expr` text and components/blocks are set per instance.
- Common bubbling events are delegated to one `document` listener per type.
- The benchmark row uses a `Template`. Creating 1,000 rows sends ~95 KB of ops instead of
  ~440 KB. Script + layout (Oz): create 1,000 rows 95.2 → 79.0 ms, replace 106.1 → 74.1,
  create 10,000 rows 1050.4 → 773.8, select 11.3 → 3.9, clear 15.8 → 12.5. The Wasm grows by
  ~5 KB raw (~1 KB Brotli) for the Counter sample.

#### Performance (fewer allocations per row)

- Signal/computation links keep the first link inline and only allocate an array from the second.
  A signal with one observer notifies it without a snapshot copy.
- Reactive text and attributes are `Effect` subclasses (`TextBinding`, `AttrBinding`) instead of
  an effect plus closures. `Effect` can be subclassed internally.
- `Attr.Static`/`Attr.Dynamic` and `OnClick(Action)` no longer wrap the callback in a closure;
  the renderer stores the `Listener` itself.
- `TemplateInstance` creates hole handles lazily and frees all hole ids in one dispose; scopes
  keep owned items in an array.
- `For`/`Each` skips the common prefix and suffix before diffing, so append, remove and swap only
  diff the changed keys. Duplicate keys are still rejected before the DOM is touched.
- Oz script time: create 10,000 rows ~420 → 271 ms, append 1,000 rows 53.6 → 45.1, reactive
  `deep` 33.4 → 14.2, `broad` 68.0 → 37.9, `create` 54.3 → 19.3. Download size unchanged.
