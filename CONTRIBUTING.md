# Contributing

## Build and test

```powershell
dotnet build NetWasm.UI.slnx
dotnet test tests/NetWasm.UI.Tests                 # core unit tests (net10.0, < 1 s)
```

Unit tests run the core on `net10.0` against `FakeDomHost`, an in-memory decoder of the
[render protocol](docs/architecture.md#render-protocol). When you add an op, update `Op.cs`,
`Renderer`, `dom.mjs` and `FakeDomHost.cs` together.

### Browser (E2E) tests

```powershell
dotnet build tests/NetWasm.UI.E2E
pwsh tests/NetWasm.UI.E2E/bin/Debug/net10.0/playwright.ps1 install chromium   # once
dotnet test tests/NetWasm.UI.E2E
```

The fixture starts the dev server (`--no-watch`, free port), waits for the first publish and runs
each test in a fresh Chromium context. Any console error or uncaught page error fails the test.

- `NETWASM_UI_E2E_URL=http://localhost:8765` reuses a server you already run.
- `HEADED=1` shows the browser.

### Template grammar tests

```powershell
cd tools/vscode-nwui
npm install
npm test
```

### CI

`.github/workflows/ci.yml` runs on `ubuntu-latest`: build, unit tests, `.nwui` grammar tests,
E2E tests, and an `Oz` publish of `samples/Counter` with a size table in the job summary.

### Benchmarks

See [docs/benchmarks.md](docs/benchmarks.md). Run them before and after changes to the
reactive core, the renderer or `dom.mjs`.

## Code guidelines

- `netwasm0.1` has no `System.Linq`, reflection or `async`. Write plain loops.
- Watch the Wasm size: avoid interpolated strings with non-string holes, `IFormattable`, new
  generic instantiations in the core and multicast delegates. See
  [Keeping the Wasm small](docs/deployment.md#keeping-the-wasm-small). The CI size table shows
  the effect of a change.
- Inside a `catch` block, do not branch before the exception is used (for example
  `catch (Exception ex) { if (x) throw; Report(ex); }`). In Release, Roslyn keeps `ex` on the
  IL stack and the NetWasm compiler fails with `NW1002: evaluation stack is not empty at control
  transfer`. Pick the path before the `try` instead (see `Effect.Run`).
- Update the docs in `docs/` and the [CHANGELOG](CHANGELOG.md) with user-visible changes.

## The dev server

`tools/DevServer` is a Kestrel server that publishes the app, serves it with Brotli/gzip and the
right MIME types, falls back to `index.html` for extension-less paths, watches sources and
reloads the browser.

### Live reload, not hot reload

NetWasm is closed-world AOT: there is no runtime to patch, so .NET Hot Reload (Edit & Continue)
cannot apply. The dev server instead watches sources, re-runs `dotnet publish` and pushes a
reload over a WebSocket. Build errors are shown as an overlay in the page. Saves that do not
change file content (editor touches, formatters) are ignored via content hashing.

Responses use `Cache-Control: no-cache` (not `no-store`), so unchanged files are revalidated with
304s and the browser can keep its compiled Wasm.

### Why a rebuild takes 15-20 s

The browser layout (`browser/*.mjs`, `netwasm.page.mjs`, `deployment.json`) is only produced by
the SDK's publish target, so a plain `build` is not enough. Measured on the Counter sample after a
one-line `.cs` change (NetWasm 0.10.0, Release):

| Step (MSBuild target)              | Oz           | Notes                                   |
|------------------------------------|-------------:|-----------------------------------------|
| `NetWasmCompileCoreModule`         |       ~7.2 s | AOT, scales with optimization level     |
| `NetWasmLinkRawModule`             |       ~5.9 s | wasm-ld + binaryen                      |
| `NetWasmSdkResolveBuildEnvironment`|       ~3.5 s | fixed: probes node/wasm-ld every run    |
| `NetWasmBuildRawBindings`          |       ~2.6 s |                                         |
| MSBuild + restore overhead         |       ~1.4 s |                                         |
| `NetWasmSdkPublishJsModule`        |       ~0.3 s | the actual "publish" part is negligible |
| **Total**                          |    **~21 s** |                                         |

| `NetWasmOptimization` | Publish after edit | `Counter.wasm` |
|-----------------------|-------------------:|---------------:|
| `Oz` (production)     |             ~21 s  |         316 KB |
| `O1` (dev default)    |             ~14 s  |         519 KB |
| `O0`                  |             ~12 s  |         615 KB |

(Measured before the `/components` sample was added; the app is bigger now, the ratios hold.)

The `Debug` configuration is slower (~32 s) and larger (869 KB), so the dev server stays on
Release. The remaining floor is in the toolchain itself (environment probing, non-incremental
AOT) and is feedback for the NetWasm team rather than something this repository can shave.

## Roadmap ideas

- **Production readiness:** NuGet packages (with the generator wired up automatically) and a
  `dotnet new` template; a public API baseline in CI.
- **Templates:** parameterized slots (`Slot<T>`), `@switch`, a language server for `.nwui`
  (IntelliSense via the generated C#).
- **Rendering speed** (see [Benchmarks](docs/benchmarks.md#still-open)): clone prebuilt
  `<template>`s for static subtrees, delegate events at the root, and push-pull `Computed` to
  avoid glitchy eager recomputes.
- Microtask-coalesced batching across multiple JS→Wasm events in one frame.
- `<Suspense>`-style async boundaries once NetWasm supports async interop; nested/layout routes;
  base-path routing.
- **Wasm size:** a size report per feature in CI; a slimmer JS host for page-only apps (needs
  NetWasm).
- **Dev loop:** snapshot/restore signal state across live reloads, CSS swap without reload,
  faster incremental NetWasm publish.
- **Production caching:** NetWasm emits stable file names but a `buildFingerprint` in
  `deployment.json`. An opt-in publish step could move `browser/` to `v/<fingerprint>/` (import
  map in `index.html`) so hosts can serve it with `Cache-Control: immutable`.
