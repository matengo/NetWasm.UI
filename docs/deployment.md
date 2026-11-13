# Publishing and size

## Release build

```powershell
dotnet publish -c Release
# → bin/Release/netwasm0.1/publish/
```

`Release` uses `NetWasmOptimization=Oz` (smallest output). The dev server builds `Debug` with `O0`
for fast incremental rebuilds, so its Wasm is several times larger; run it with `--release` to see
production sizes.

## What to deploy

Deploy your content files (`index.html`, `app.mjs`, CSS, images) and the whole `browser/`
folder. The `*.dll` and `*.pdb` files in the publish root are build artifacts and are never
downloaded. `deployment.json` lists every artifact the runtime may load, with its SHA-256.

A first visit downloads these files:

- `index.html` and `app.mjs`;
- `browser/<App>.mjs`;
- `netwasm.page.mjs` and the `netwasm.runtime.*.mjs` chunks (NetWasm's JS host, with `dom.mjs`
  bundled in);
- `deployment.json`;
- the raw adapter;
- `<App>.wasm`.

## Startup preloading

Without hints, the browser discovers these files one import level at a time: `app.mjs` →
`<App>.mjs` → `netwasm.page.mjs` → the runtime chunks → `deployment.json`, and only then the
Wasm. That is about seven round-trips before the largest file starts downloading. Compiling and
instantiating the Wasm takes only a few milliseconds by comparison.

So `NwUi.Browser` rewrites the published `index.html` after `dotnet publish`. Before `</head>` it
adds `<link rel="modulepreload">` for every module in `browser/`, and
`<link rel="preload" as="fetch" crossorigin>` for `deployment.json`, the Wasm, the raw adapter
and the JSON sidecars. Everything then downloads in parallel from the first response:

```html
  <!-- nwui:preload -->
  <link rel="preload" as="fetch" crossorigin href="/browser/deployment.json" />
  <link rel="preload" as="fetch" crossorigin href="/browser/MyApp.wasm" />
  ...
  <link rel="modulepreload" href="/browser/netwasm.runtime.page.BV4DeFLi.mjs" />
  <!-- /nwui:preload -->
```

The block is regenerated on every publish, because the runtime chunk names are content-hashed.
Links start with `/`, or are relative when the page has a `<base href>`.

Measured with `samples/Counter` (Chromium, uncompressed static server, median of runs):

| Network | Without preload | With preload |
|---|---:|---:|
| Local | Wasm request at ~290 ms, first render ~470 ms | Wasm request at ~25 ms, first render ~310 ms |
| 150 ms RTT, 1.6 Mbit/s, CPU ×4 | Wasm request at ~2,550 ms, first render ~5.9 s | Wasm request at ~190 ms, first render ~4.7 s |

On the slow network, bandwidth becomes the limit once the waterfall is gone. Brotli matters more
than anything else there (see [Hosting](#hosting)).

| Property | Default | Meaning |
|---|---|---|
| `NetWasmUIPreload` | `true` | Set to `false` to leave `index.html` untouched. |
| `NetWasmUIPreloadHtml` | `$(PublishDir)index.html` | The page to rewrite. Skipped if it does not exist. |
| `NetWasmUIPreloadUrlPrefix` | `/`, or empty with `<base href>` | Prefix for the link URLs. |

## Size

This is the whole demo app in Release with NetWasm.Sdk 0.11.1. It has a counter, todos, the
router with nested routes, an async form and `/components`:

| Artifact | Raw | Brotli | Gzip |
|---|---:|---:|---:|
| `Counter.wasm` | 616 KB | 108 KB | 149 KB |
| NetWasm JS host (`netwasm.page.mjs`, runtime chunks, adapter) | 217 KB | 48 KB | 55 KB |
| **Whole page** | **833 KB** | **157 KB** | **204 KB** |

The same code built with NetWasm.Sdk 0.10.7 gave a 592 KB raw / 101 KB Brotli `Counter.wasm`.

About 15–20 KB of the Wasm is the `async`/`Task` machinery that the form's async submit pulls
in. An app without `async` is correspondingly smaller. See [Benchmarks](benchmarks.md) for a
comparison with SolidJS, React and Blazor.

To measure your own app:

```shell
node tools/size-report/size-report.mjs bin/Release/netwasm0.1/publish --name MyApp
```

### Size budget

CI publishes `samples/Counter` with `Oz` and fails when it exceeds the limits in
[`size-budget.json`](../size-budget.json). If a pull request grows the app on purpose, raise the
budget in the same pull request and say why. `--write-budget` writes the current sizes plus 5 %.

## Hosting

Any static file host works if it does three things:

1. **MIME types**: `.wasm` as `application/wasm`, `.mjs` as `text/javascript`, `.json` as
   `application/json`. Browsers refuse modules served with the wrong type.
2. **Compression**: serve Brotli (or at least gzip). Without it the page is about 5× larger.
   Pre-compress at deploy time (`brotli -q 11`) if the host does not compress on the fly.
3. **SPA fallback**: return `index.html` for extension-less paths that are not files, so deep
   links like `/todos/3` work. Asset URLs in `index.html` and `app.mjs` must be absolute
   (`/app.mjs`, `/browser/App.mjs`), or relative to a `<base href>`.

To serve the app under a sub-path, see [base path](routing.md#hosting-and-base-path). Also send
a Content Security Policy; see [Security](security.md#content-security-policy) for one that
works.

Examples:

```nginx
# nginx
types { application/wasm wasm; text/javascript mjs; }
brotli_static on;   # or gzip_static on;
location / { try_files $uri /index.html; }
```

```csharp
// ASP.NET Core: serve the publish folder
var app = WebApplication.Create(args);
var types = new FileExtensionContentTypeProvider();
types.Mappings[".mjs"] = "text/javascript";
types.Mappings[".wasm"] = "application/wasm";
app.UseResponseCompression();      // after builder.Services.AddResponseCompression(o => o.EnableForHttps = true)
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = types });
app.MapFallbackToFile("index.html");
app.Run();
```

`tools/DevServer` (`nwui-dev`) is a working Kestrel reference with compression, MIME types and
fallback. It is a development server; do not use it in production.

### GitHub Pages

Pages serves files from a sub-path (`https://<user>.github.io/<repo>/`), gzips on the fly and
has no SPA fallback, only a custom `404.html`. So, after publishing:

1. Set `<base href="/<repo>/" />` in `index.html` and keep every asset URL relative.
2. Copy `index.html` to `404.html`: an unknown path then loads the app, and the router renders
   it (the response status is still 404).
3. Add an empty `.nojekyll` and upload the folder without `*.dll` and `*.pdb`.

[`.github/workflows/pages.yml`](../.github/workflows/pages.yml) does this for the project
website in [`site/`](../site/).

## Caching

NetWasm's runtime chunks have hashed names (`netwasm.runtime.<hash>.mjs`), but the app's own
files have **stable names** (`browser/App.wasm`, `browser/App.mjs`), so a long
`Cache-Control: max-age` would keep old code after a deploy. Until fingerprinted output is
supported, use `Cache-Control: no-cache` with ETags: browsers revalidate (cheap 304s) and only
download what changed. The hashed `netwasm.runtime.*.mjs` chunks can be served with
`Cache-Control: public, max-age=31536000, immutable`.

Browsers recompile the Wasm on every visit. The NetWasm runtime verifies each artifact's SHA-256
before it calls `WebAssembly.compile`, so the browser's compiled-code cache (which needs
`WebAssembly.compileStreaming`) does not apply. For `samples/Counter` the compile takes about
6 ms, so this costs little.

`browser/deployment.json` contains a `buildFingerprint`. A planned opt-in publish step will move
`browser/` to `v/<fingerprint>/` so hosts can use `Cache-Control: immutable`.

## Keeping the Wasm small

NetWasm compiles a closed world ahead of time, so code size follows what is *reachable*:

- **Avoid `$"…{x}"` with non-string holes in hot paths of library code.** NetWasm's
  interpolation handler goes through `StringBuilder.FormatValue`, which reaches
  `double`/`float`/`decimal` formatting (~60 KB raw). `"width: " + n.ToString() + "%"` costs
  almost nothing.
- **Interface calls make every implementation reachable.** `((IFormattable)x).ToString(…)` pulls
  in all number formatting. `TemplateRuntime.Str<T>` uses `typeof(T)` checks instead, which fold
  away per instantiation.
- **Generic code is not shared.** Each `For<T, TKey>` used to carry its own `Dictionary`,
  `HashSet` and diff (~30 KB raw each); the diff now lives in a non-generic `KeyedList` over boxed
  keys.
- **Avoid culture-aware and case-insensitive string APIs.** `ToLowerInvariant`,
  `char.IsWhiteSpace`/`IsLetter` and any `IndexOf(string, …, StringComparison)` (its switch
  includes the `OrdinalIgnoreCase` path) pull in Unicode casing/category tables. A first version
  of the `Template` scanner grew the benchmark Wasm by ~110 KB raw; ASCII loops brought that down
  to ~12 KB.
- **Prefer `NetWasm.UI.Routing.Url.Encode/Decode`** over `Uri.EscapeDataString`/
  `UnescapeDataString` (~40 KB raw).
- **`event`s with `+=`** pull in multicast delegates; the framework keeps handlers in a `List`.

These changes took the demo's `Counter.wasm` from 645 KB (102 KB Brotli) to 463 KB (77 KB Brotli).

To find what is big, look at `bin/Release/netwasm0.1/<App>.core.wasm` (before `wasm-opt`, with a
name section) and run `wasm-opt <core.wasm> --print-call-graph` (from the `netwasm.hosttools`
package).
