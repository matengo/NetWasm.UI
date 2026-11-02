# Benchmarks

`benchmarks/` compares NetWasm.UI with SolidJS, React and Blazor WebAssembly on the same app.

| Folder | What |
|---|---|
| `NetWasmBench` | The js-framework-benchmark "keyed" table app (create/replace/update/select/swap/remove/clear rows) plus DOM-free workloads (`ReactiveBench.cs`, `PlainBench.cs`). |
| `solid` | The same app (based on the official keyed Solid implementation, with `createSelector`) and the same workloads in JS, built with Vite. |
| `react` | The official keyed `react-hooks` implementation (React 19, `memo` rows), built with Vite. |
| `BlazorBench` | The same app in Blazor WebAssembly (.NET 10, row components with `ShouldRender`, `InvariantGlobalization`), published both as default (interpreter) and with `RunAOTCompilation`. It links `Labels.cs` and `PlainBench.cs` from NetWasmBench, so `sort`/`sieve` run the same C#. |
| `Runner` | Publishes and builds all apps, serves them with Kestrel and drives Chromium with Playwright. |

## Running

```powershell
dotnet run --project benchmarks/Runner -c Release -- --runs 10
# options: --skip-build, --no-o3, --no-aot, --headed
```

Blazor AOT needs the `wasm-tools` workload (`dotnet workload install wasm-tools`) and takes a few
minutes to build; skip it with `--no-aot`.

## Method

- Every DOM case opens a fresh page, runs the setup clicks, forces a GC and then times one
  `element.click()` until the DOM check passes (row count, labels, selected row).
- NetWasm.UI and Solid finish inside the click. React and Blazor may render after it, so the
  runner then waits on microtasks/macrotasks until the check passes.
- *+ layout* adds a forced layout (`document.body.offsetHeight`). Paint is not measured.
- DOM-free workloads return checksums that must match across implementations. React and Blazor
  have no signals, so the reactive workloads are "–" for them (React's `sort`/`sieve` would be
  Solid's JS again).
- *Download* sums every response of a first visit, compressed with Brotli quality 11.
- Values are medians in ms. Cases under ~10 ms move by a few ms between runs.

## Results

Windows arm64 laptop, Chromium 153, 10 runs, script + layout:

| Case                         | NetWasm.UI Oz | NetWasm.UI O3 | SolidJS |  React | Blazor | Blazor AOT |
|------------------------------|--------------:|--------------:|--------:|-------:|-------:|-----------:|
| create 1,000 rows            |          95.2 |          90.6 |    52.6 |   83.2 |  248.6 |      150.8 |
| replace 1,000 rows           |         106.1 |          97.1 |    68.8 |   98.4 |  297.8 |      176.4 |
| partial update (every 10th)  |          24.4 |          25.8 |    27.1 |   33.3 |  119.3 |       55.7 |
| select row                   |          11.3 |           5.1 |     4.1 |   29.3 |   97.4 |       39.2 |
| swap rows                    |           7.6 |           6.8 |     6.5 |   45.0 |   60.0 |       17.5 |
| remove row                   |           7.6 |           7.0 |     5.9 |    9.3 |   64.8 |       18.2 |
| create 10,000 rows           |        1050.4 |         983.7 |   436.3 |  957.1 | 1895.2 |     1483.9 |
| append 1,000 rows            |         110.2 |         103.5 |    49.7 |   74.8 |  285.4 |      203.8 |
| clear 1,000 rows             |          15.8 |          15.2 |     9.5 |   14.3 |   47.5 |       16.5 |
| reactive: deep (100-chain)   |          32.6 |          17.0 |    19.3 |      – |      – |          – |
| reactive: broad (1,000 fan-out) |       65.7 |          34.5 |    34.9 |      – |      – |          – |
| reactive: diamond            |          18.5 |           8.2 |     3.9 |      – |      – |          – |
| reactive: create/dispose 10k |          46.8 |          35.4 |    14.0 |      – |      – |          – |
| sort 100k strings (no UI)    |         134.4 |         130.7 |   125.8 |      – |  340.7 |       92.5 |
| sieve 2M (no UI)             |          13.2 |          11.6 |    67.7 |      – |   35.2 |       44.3 |
| startup (`__ready`)          |         126.3 |         143.9 |    25.5 |   54.8 | 1142.2 |     1241.4 |
| download, Brotli (KB)        |         120.7 |         139.3 |     6.2 |   58.4 | 1442.8 |     3163.0 |

In short: NetWasm.UI is roughly level with React (faster at updates, select and swap, slower at
creating rows), 1–2× Solid, and 2–10× faster than Blazor with a ~12× smaller download and ~9×
faster startup. The pure-compute rows (`sort`, `sieve`) vary between runs because Chromium tiers
Wasm and JS up at different times (Liftoff → TurboFan); treat them as "same ballpark".

The fixes made after the first run are listed in the [CHANGELOG](../CHANGELOG.md).

## Still open

- Creating rows costs ~2× Solid. Every node is a `createElement` op plus one op per attribute,
  text and listener, while Solid clones a prebuilt `<template>` and delegates events. Template
  cloning for static subtrees and event delegation are the next steps.
- `Computed` recomputes eagerly on every dirty notification, so a diamond recomputes its join
  once per branch (`diamond`). A push-pull model (mark stale, recompute on read) would fix it.
- `O3` is ~1.5–2× faster than `Oz` on reactive work but makes the Wasm bigger. `Oz` stays the
  default.
- Startup includes downloading and compiling ~460 KB of Wasm vs. 17 KB of JS.
