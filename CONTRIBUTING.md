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
- `E2E_BROWSER=firefox` or `E2E_BROWSER=webkit` runs the tests in another engine. The default
  is `chromium`. Install the engine first with `playwright.ps1 install firefox`.

### Template grammar tests

```powershell
cd tools/vscode-nwui
npm install
npm test
```

### CI

`.github/workflows/ci.yml` runs on `ubuntu-latest` and has three jobs:

- **`test`**: the build, the unit tests, the `.nwui` grammar tests with the packaged VS Code
  extension, the Chromium E2E tests, and an `Oz` publish of `samples/Counter` checked against
  [`size-budget.json`](size-budget.json).
- **`e2e-browsers`**: the E2E tests in Firefox and WebKit.
- **`packages`**: packs the NuGet packages, then creates, builds and publishes an app from the
  template against them.

`.github/workflows/pages.yml` publishes the website in `site/` to GitHub Pages on every push to
`main` that touches `site/` or `src/`. It needs Settings → Pages → Source = "GitHub Actions".

### Size budget

The `test` job fails when the Counter sample exceeds `size-budget.json`. If your change makes it
bigger on purpose, update the budget in the same pull request and explain why:

```powershell
dotnet publish samples/Counter -c Release -p:NetWasmOptimization=Oz
node tools/size-report/size-report.mjs samples/Counter/bin/Release/netwasm0.1/publish --name Counter --write-budget size-budget.json
```

### Public API

`NetWasm.UI` and `NetWasm.UI.Browser` use the public API analyzer. A new public member fails
the build with `RS0016` until it is listed in that project's `PublicAPI.Unshipped.txt`. Copy the
signature from the build error; `dotnet format analyzers --diagnostics RS0016` does not handle
every case.

At a release, the `Unshipped` lines move to `PublicAPI.Shipped.txt`. Removing or changing a
shipped member is a breaking change; list it in the CHANGELOG.

### Packages and the template locally

```powershell
$feed = "$env:TEMP\nwfeed"
dotnet pack src/NetWasm.UI -c Release -o $feed
dotnet pack src/NetWasm.UI.Browser -c Release -o $feed
dotnet pack tools/DevServer -c Release -o $feed
dotnet pack templates/NetWasm.UI.Templates -c Release -o $feed
dotnet new install "$feed\NwUi.Templates.0.8.0.nupkg"
dotnet new netwasm-ui -o $env:TEMP\MyApp --skipRestore
dotnet restore $env:TEMP\MyApp -s $feed -s https://api.nuget.org/v3/index.json
```

NuGet caches packages by version. Clear `~/.nuget/packages/nwui*` before you test a
rebuilt package with the same version.

### Releasing

1. Update the [CHANGELOG](CHANGELOG.md) and move `PublicAPI.Unshipped.txt` into
   `PublicAPI.Shipped.txt`.
2. Bump `VersionPrefix` in `Directory.Build.props`, and the versions the template pins in
   `templates/NetWasm.UI.Templates/templates/netwasm-ui/NetWasmApp.csproj` and
   `.config/dotnet-tools.json`.
3. Push a tag `v<version>` (for example `v0.1.0`). `.github/workflows/release.yml` builds, tests,
   publishes the packages to NuGet and the extension to the VS Code Marketplace, and creates a
   GitHub release.

Publishing needs, once: a Trusted Publishing policy on nuget.org (owner `matengo`, repository
`NetWasm.UI`, workflow file `release.yml`), the repository variable `NUGET_USER` (your nuget.org
profile name) and the secret `VSCE_PAT` for the Marketplace. Without them the step is skipped.

### Benchmarks

See [docs/benchmarks.md](docs/benchmarks.md). Run them before and after changes to the
reactive core, the renderer or `dom.mjs`.

### The website

`site/` is the project website, a NetWasm.UI app. Run it with
`dotnet run --project tools/DevServer -- site`. Each live example is a `.nwui` file in
`site/Demos`, published next to the app as `source/<name>.nwui.txt` so the page can show its
own source. The benchmark table in `site/Pages/BenchmarksPage.nwui` is copied from
`docs/benchmarks.md`; update both together.

## Code guidelines

- `netwasm0.1` has no reflection. Do not use `System.Linq` in the libraries; write plain loops.
  `async` works, but its first use costs about 15–20 KB Brotli, so keep it out of the core
  unless a feature needs it.
- Watch the Wasm size: avoid interpolated strings with non-string holes, `IFormattable`, new
  generic instantiations in the core and multicast delegates. Avoid culture-aware or
  case-insensitive string APIs (`ToLowerInvariant`, `StringComparison.OrdinalIgnoreCase`,
  `char.IsWhiteSpace`/`IsLetter`, even `IndexOf(string, StringComparison)`): they pull in Unicode
  tables (~100 KB raw). Use ASCII loops instead (see `Template.Scan`). See
  [Keeping the Wasm small](docs/deployment.md#keeping-the-wasm-small). The CI size table shows
  the effect of a change.
- Inside a `catch` block, do not branch before the exception is used (for example
  `catch (Exception ex) { if (x) throw; Report(ex); }`). In Release, Roslyn keeps `ex` on the
  IL stack and the NetWasm compiler fails with `NW1002: evaluation stack is not empty at control
  transfer`. Pick the path before the `try` instead (see `Effect.Run`).
- Update the docs in `docs/` and the [CHANGELOG](CHANGELOG.md) with user-visible changes.

## The dev server

`tools/DevServer` is packed as the .NET tool `NwUi.DevServer` (command `nwui-dev`). In
this repository, run it with `dotnet run --project tools/DevServer -- <app>`. It is a Kestrel
server that publishes the app, serves it with Brotli/gzip and the
right MIME types, falls back to `index.html` for extension-less paths, watches sources and
reloads the browser.

### Live reload, not hot reload

NetWasm is closed-world AOT: there is no runtime to patch, so .NET Hot Reload (Edit & Continue)
cannot apply. The dev server instead watches sources, re-runs `dotnet publish` and pushes a
reload over a WebSocket. Build errors are shown as an overlay in the page. Saves that do not
change file content (editor touches, formatters) are ignored via content hashing.

State survives the reload. In `Debug`, `DevStateGenerator` emits a `[ModuleInitializer]`
that calls `DevReload.Install()`. If the page has the reload client (`globalThis.__nwuiDev`), it
sets the static `DevState.Tracker` delegate, which every `Signal<T>` constructor invokes; the
tracker numbers signals in creation order and restores saved `int`/`bool`/`string` values whose
initial value still matches. Before reloading, `reload-client.js` asks each app for its
serialized signals and saves them, plus form fields, focus and scroll, in `sessionStorage`.
Release builds never assign the delegate, so the tracker code is trimmed. NetWasm does not remove
dead branches, so dev-only code must be unreachable rather than behind an `if`.

Responses use `Cache-Control: no-cache` (not `no-store`), so unchanged files are revalidated with
304s and the browser can keep its compiled Wasm.

### Rebuild time

The browser layout (`browser/*.mjs`, `netwasm.page.mjs`, `deployment.json`) is only produced by
the SDK's publish target, so the dev server runs `dotnet publish` (a plain `build` is not faster).
Since NetWasm 0.11, a `Debug` build with `O0` keeps each method's compiled code and recompiles
only the methods that changed; that is the dev server default. In watch mode it also keeps the
MSBuild nodes and the compiler server warm.

Counter sample, one-line `.cs` change, NetWasm.Sdk 0.11.1:

| Configuration | `dotnet publish` after edit | Save → reloaded page |
|---|---:|---:|
| `Debug`, `O0` (dev default, warm build servers) | ~2.5–3.5 s | ~3.5–4 s |
| `Release`, `O1` (dev default before 0.11) | ~7 s | ~8 s |
| `Release`, `Oz` (production) | ~12 s | – |

Of the ~3 s, the NetWasm compile itself is ~0.9 s; the rest is MSBuild evaluation, project
references, linking and bindings. The `Debug`/`O0` Wasm is about 3× larger than `Oz`, which does
not matter on localhost.

## Roadmap ideas

- **Templates:** parameterized slots (`Slot<T>`), `@switch`, a language server for `.nwui`
  (IntelliSense via the generated C#).
- **Rendering speed** (see [Benchmarks](docs/benchmarks.md#still-open)): push-pull `Computed` to
  avoid glitchy eager recomputes; cheaper per-row Wasm work (fewer allocations per effect).
- Microtask-coalesced batching across multiple JS→Wasm events in one frame.
- **Accessibility:** focus management after navigation, announcing route changes.
- **Wasm size:** a size report per feature in CI; a slimmer JS host for page-only apps (needs
  NetWasm).
- **Dev loop:** keep list/object signals across live reloads, CSS swap without reload,
  faster incremental NetWasm publish.
- **Production caching:** NetWasm emits stable file names but a `buildFingerprint` in
  `deployment.json`. An opt-in publish step could move `browser/` to `v/<fingerprint>/` (import
  map in `index.html`) so hosts can serve it with `Cache-Control: immutable`.
