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

Windows arm64 laptop, Chromium 153, 10 runs, script + layout, NetWasm.Sdk 0.13.0 with NetWasmGC,
NetWasm.UI 0.9.0. Blazor AOT was not re-measured (`--no-aot`); an earlier run had it at
142.3 ms for create 1,000 rows and 1,400 ms startup.

| Case                         | NetWasm.UI Oz | NetWasm.UI O3 | SolidJS |  React | Blazor |
|------------------------------|--------------:|--------------:|--------:|-------:|-------:|
| create 1,000 rows            |          54.7 |          59.6 |    71.0 |   67.7 |  236.3 |
| replace 1,000 rows           |          62.3 |          65.8 |    62.7 |   81.8 |  263.3 |
| partial update (every 10th)  |          26.5 |          22.1 |    22.5 |   27.1 |   88.9 |
| select row                   |           4.8 |           5.4 |     3.2 |   14.2 |   95.1 |
| swap rows                    |           7.5 |           8.3 |     9.0 |   61.2 |   69.0 |
| remove row                   |           5.0 |           5.3 |     6.0 |   10.3 |   80.6 |
| create 10,000 rows           |         588.9 |         557.8 |   489.2 | 1041.7 | 2405.2 |
| append 1,000 rows            |          78.5 |          88.5 |    62.5 |  113.8 |  302.0 |
| clear 1,000 rows             |          15.8 |          20.5 |    10.1 |   20.6 |   81.8 |
| reactive: deep (100-chain)   |           9.3 |           9.2 |    21.0 |      – |      – |
| reactive: broad (1,000 fan-out) |        28.1 |          21.5 |    35.3 |      – |      – |
| reactive: diamond            |           2.6 |           1.9 |     4.3 |      – |      – |
| reactive: create/dispose 10k |           7.8 |           6.1 |    18.1 |      – |      – |
| sort 100k strings (no UI)    |          60.1 |          57.7 |   132.2 |      – |  643.6 |
| sieve 2M (no UI)             |           6.8 |           7.8 |   130.1 |      – |   67.5 |
| startup (`__ready`)          |         127.9 |         133.7 |    25.4 |   57.0 | 1204.6 |
| download, Brotli (KB)        |         114.0 |         128.0 |     6.2 |   58.4 | 2087.3 |

Script time alone (without layout), Oz vs. Solid: create 1,000 rows 16.9 vs 16.3, create 10,000
rows 206.0 vs 112.2, append 32.8 vs 21.8, replace 28.8 vs 23.4.

In short: NetWasm.UI is level with or faster than React on every DOM case, level with or faster
than Solid at create 1,000 rows, replace, swap, remove and every reactive and compute workload,
1.2–1.6× Solid elsewhere, and 3–20× faster than Blazor (interpreter) with a ~18× smaller
download and ~9× faster startup. Cases under ~10 ms, and partial update, move by a few ms
between runs. The pure-compute rows vary between runs because Chromium tiers Wasm and JS up at
different times (Liftoff → TurboFan).

### NetWasmGC vs. Compact

The same run with `--gc NetWasm,Compact` builds the Oz app with each collector. Script time in
ms; NetWasmGC is the default for the template, samples and benchmark since 0.9.0:

| Case                         | NetWasmGC | Compact |
|------------------------------|----------:|--------:|
| create 1,000 rows            |      17.1 |    14.6 |
| replace 1,000 rows           |      33.3 |    28.5 |
| create 10,000 rows           |     204.7 |   221.0 |
| append 1,000 rows            |      30.8 |    36.5 |
| clear 1,000 rows             |      12.5 |    21.0 |
| reactive: create/dispose 10k |       8.0 |    17.7 |
| sort 100k strings (no UI)    |      61.3 |    97.3 |
| sieve 2M (no UI)             |       6.5 |     9.3 |
| download, Brotli (KB)        |     114.0 |   112.4 |

Allocation-heavy work is clearly faster with NetWasmGC; the 1,000-row create and replace cases
are within noise. See [#2](https://github.com/matengo/NetWasm.UI/issues/2) for runs on Linux.

Compared with the previous run (NetWasm.Sdk 0.10.7, Compact), create 1,000 rows went from 57.0
to 54.7 ms, create 10,000 rows from 624.9 to 588.9, append from 98.6 to 78.5, and the compute
workloads by 30–60 %.
The fixes made after earlier runs are listed in the [CHANGELOG](../CHANGELOG.md).

## Still open

- Creating many rows costs 1.2–1.3× Solid with layout and up to 1.8× in script time, growing
  with the list size. The browser's DOM work is the same as Solid's; the gap is per-row work in
  C# and in the batch-applying JavaScript (see the profile in
  [#2](https://github.com/matengo/NetWasm.UI/issues/2)). 0.9.0 removed the `params` array in
  `With` and the `Map` lookup in `nwui.dom`. Next: handlers that take data instead of a closure,
  numbers written straight into text holes, generated per-template walkers, and fewer objects
  per row (typed keys, a lazy row `Scope`, cheaper `Element` handles).
- `O3` is faster than `Oz` on some reactive work but not on most DOM cases, and grows the total
  Brotli download by ~12 % (114 → 128 KB). `Oz` stays the default.
- Startup includes downloading and compiling the Wasm (~470 KB raw download in total vs. 17 KB of
  JS for Solid). On a local server the download and compile take only a few ms. Most of the time
  goes to the serial module chain before the Wasm request, which
  [startup preloading](deployment.md#startup-preloading) removes on a real network.
