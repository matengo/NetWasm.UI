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
#          --apps netwasm-oz,solid   only these apps (output folders under benchmarks/.out)
#          --cases create,diamond    only cases/workloads whose name contains one of these
#          --gc NetWasm,Compact      also run the Oz app built with each of these NetWasm collectors
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

Windows arm64 laptop, Chromium 153, 10 runs, script + layout, NetWasm.Sdk 0.10.7, NetWasm.UI
after 0.1.0 (push-pull `Computed`). Blazor AOT was not re-measured (`--no-aot`); an earlier run
had it at 142.3 ms for create 1,000 rows and 1,400 ms startup.

| Case                         | NetWasm.UI Oz | NetWasm.UI O3 | SolidJS |  React | Blazor |
|------------------------------|--------------:|--------------:|--------:|-------:|-------:|
| create 1,000 rows            |          57.0 |          60.3 |    60.7 |   77.1 |  251.9 |
| replace 1,000 rows           |          64.7 |          67.7 |    58.1 |   86.1 |  276.4 |
| partial update (every 10th)  |          23.9 |          40.5 |    25.1 |   34.9 |   93.9 |
| select row                   |           4.5 |           4.0 |     3.3 |   13.8 |   94.1 |
| swap rows                    |           6.6 |           7.8 |     7.4 |   59.9 |   61.4 |
| remove row                   |           6.3 |           5.6 |     6.3 |   11.0 |   72.5 |
| create 10,000 rows           |         624.9 |         602.1 |   512.4 |  996.4 | 2652.0 |
| append 1,000 rows            |          98.6 |          99.3 |    60.2 |   95.7 |  327.8 |
| clear 1,000 rows             |          13.6 |          14.3 |    10.3 |   16.7 |   65.7 |
| reactive: deep (100-chain)   |          12.1 |           9.5 |    23.1 |      – |      – |
| reactive: broad (1,000 fan-out) |       32.1 |          27.3 |    38.3 |      – |      – |
| reactive: diamond            |           2.6 |           2.3 |     3.5 |      – |      – |
| reactive: create/dispose 10k |          17.5 |          17.4 |    13.5 |      – |      – |
| sort 100k strings (no UI)    |          99.0 |          99.5 |   124.6 |      – |  645.5 |
| sieve 2M (no UI)             |           9.4 |          11.6 |   125.3 |      – |   47.2 |
| startup (`__ready`)          |         131.2 |         133.6 |    25.9 |   64.2 | 1209.9 |
| download, Brotli (KB)        |         117.6 |         130.3 |     6.2 |   58.4 | 1442.2 |

Script time alone (without layout), Oz vs. Solid: create 1,000 rows 17.9 vs 14.6, create 10,000
rows 255.7 vs 124.5, append 53.1 vs 19.0, replace 32.3 vs 21.3.

`diamond` went from 7.8 to 2.6 ms with the push-pull `Computed` (the join now recomputes once per
change instead of once per branch); the other cases are within run-to-run noise of the 0.1.0 run.

In short: NetWasm.UI is level with or faster than React on every DOM case, level with or faster
than Solid at create 1,000 rows, partial update, swap, remove and the deep/broad/diamond reactive
workloads, 1.1–1.6× Solid elsewhere, and 3–21× faster than Blazor (interpreter) with a ~12×
smaller download and ~9× faster startup.
The pure-compute rows vary between runs because Chromium tiers Wasm and JS up at different
times (Liftoff → TurboFan).

The fixes made after earlier runs are listed in the [CHANGELOG](../CHANGELOG.md).

## Still open

- Creating many rows costs 1.2–1.6× Solid with layout and 2–3× in script time, growing with the
  list size. Profiling shows the managed part dominates, and microbenchmarks show that each
  allocation gets slower as the live heap grows (NetWasm's GC), so the ~20 objects per row (the
  row's closures, a `TemplateInstance` with node handles, one binding per reactive hole, two
  listeners, a scope and a boxed key) cost more in a large list. Removing a few of them (no
  `params` array in `With`, reused diffing collections) made no measurable difference; a real
  improvement needs fewer objects per row by design or a faster GC in NetWasm.
- `O3` is ~1.2–1.4× faster than `Oz` on reactive work and level with or faster on most DOM
  cases, but the Wasm grows from ~306 to ~536 KB raw (+11 % total Brotli download). `Oz` stays
  the default.
- Startup includes downloading and compiling ~306 KB of Wasm vs. 17 KB of JS. On a local server
  the download and compile take only a few ms. Most of the time goes to the serial module chain
  before the Wasm request, which [startup preloading](deployment.md#startup-preloading) (added
  after these runs) removes.
