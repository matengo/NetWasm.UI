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
- Script time is stable within a few percent between runs. Script + layout is not: even on an
  idle machine it moves by 10–30 % between runs, because the browser's layout dominates. In the
  run below, two identical builds of the Oz app measured 67.3 and 53.1 ms for append 1,000 rows.
  Compare script times when judging a change to the framework.
- For stable numbers, close other apps, run on mains power with the "Best performance" power
  mode, use `--runs 20`, and run twice to see how much the cases move.

## Results

Windows arm64 laptop on an otherwise idle machine, Chromium 153, script + layout, NetWasm.Sdk
0.13.0 with NetWasmGC, NetWasm.UI 0.9.0. Each value is the mean of the medians of two runs with
20 runs per case (`--runs 20 --gc NetWasm,Compact`). Blazor AOT was not re-measured
(`--no-aot`); an earlier run had it at 142.3 ms for create 1,000 rows and 1,400 ms startup.

| Case                         | NetWasm.UI Oz | NetWasm.UI O3 | SolidJS |  React | Blazor |
|------------------------------|--------------:|--------------:|--------:|-------:|-------:|
| create 1,000 rows            |          45.8 |          46.2 |    38.9 |   57.2 |  202.5 |
| replace 1,000 rows           |          52.0 |          65.0 |    51.1 |   76.6 |  247.8 |
| partial update (every 10th)  |          20.0 |          19.3 |    19.6 |   25.4 |   76.0 |
| select row                   |           3.2 |           3.5 |     2.6 |    9.4 |   68.0 |
| swap rows                    |           5.3 |           5.8 |     5.2 |   40.9 |   47.6 |
| remove row                   |           4.3 |           4.8 |     5.1 |    7.8 |   52.7 |
| create 10,000 rows           |         475.4 |         474.3 |   408.8 |  824.0 | 1876.0 |
| append 1,000 rows            |          67.3 |          70.5 |    46.2 |   65.4 |  234.8 |
| clear 1,000 rows             |           9.1 |           9.0 |     7.4 |   13.6 |   43.0 |
| reactive: deep (100-chain)   |           7.8 |           8.4 |    22.1 |      – |      – |
| reactive: broad (1,000 fan-out) |        27.1 |          21.1 |    37.9 |      – |      – |
| reactive: diamond            |           2.2 |           1.9 |     4.2 |      – |      – |
| reactive: create/dispose 10k |           7.4 |           5.7 |    15.9 |      – |      – |
| sort 100k strings (no UI)    |          46.0 |          51.3 |   107.3 |      – |  245.2 |
| sieve 2M (no UI)             |           6.2 |           8.1 |    55.5 |      – |   30.8 |
| startup (`__ready`)          |         107.3 |         105.9 |    22.0 |   46.8 |  915.9 |
| download, Brotli (KB)        |         114.0 |         128.0 |     6.2 |   58.4 | 2087.3 |

Script time alone (without layout), Oz vs. Solid: create 1,000 rows 11.7 vs 7.7, create 10,000
rows 172.4 vs 90.6, append 23.1 vs 12.8, replace 21.5 vs 17.2.

In short: NetWasm.UI is level with or faster than React on every DOM case, level with or faster
than Solid at replace, partial update, swap, remove and every reactive and compute workload,
1.2–1.5× Solid elsewhere, and 3–20× faster than Blazor (interpreter) with a ~18× smaller
download and ~9× faster startup. The append row is the noisiest: the identical NetWasmGC build
in the same runs measured 53.1 ms. The pure-compute rows vary between runs because Chromium
tiers Wasm and JS up at different times (Liftoff → TurboFan).

### NetWasmGC vs. Compact

The same runs with `--gc NetWasm,Compact` build the Oz app with each collector the same way.
Script time in ms, mean of both runs; NetWasmGC is the default for the template, samples and
benchmark since 0.9.0:

| Case                         | NetWasmGC | Compact |
|------------------------------|----------:|--------:|
| create 1,000 rows            |      12.0 |    11.4 |
| replace 1,000 rows           |      21.4 |    22.8 |
| create 10,000 rows           |     178.2 |   189.6 |
| append 1,000 rows            |      22.4 |    26.9 |
| clear 1,000 rows             |       8.9 |    11.2 |
| reactive: create/dispose 10k |       7.6 |    14.0 |
| sort 100k strings (no UI)    |      44.5 |    71.7 |
| sieve 2M (no UI)             |       6.0 |     7.8 |
| download, Brotli (KB)        |     114.1 |   112.4 |

Allocation-heavy work (sort, reactive create/dispose, clear, large lists) is clearly faster with
NetWasmGC; create and replace 1,000 rows are level. See
[#2](https://github.com/matengo/NetWasm.UI/issues/2) for runs on Linux.

An earlier run on a busy machine had Compact ahead at create 1,000 rows (14.6 vs 17.1 ms); the
quiet runs above show that was noise. The fixes made after earlier runs are listed in the
[CHANGELOG](../CHANGELOG.md).

## Still open

- Creating many rows costs 1.2–1.5× Solid with layout and up to 1.9× in script time, growing
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
