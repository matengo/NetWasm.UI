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
  idle machine it moves by 10–30 % between runs, because the browser's layout dominates. In an
  earlier run, two identical builds of the Oz app measured 67.3 and 53.1 ms for append 1,000 rows.
  Compare script times when judging a change to the framework.
- For stable numbers, close other apps, run on mains power with the "Best performance" power
  mode, use `--runs 20`, and run twice to see how much the cases move.

## Results

Windows arm64 laptop on an otherwise idle machine in the "Best performance" power mode, Chromium
from Playwright 1.63, script + layout, NetWasm.Sdk 0.14.2 with NetWasmGC, NetWasm.UI after
0.10.0 (with `SetTextInt` and `RemoveRange`). Each value is the mean of the medians of two runs
with 20 runs per case (`--runs 20 --no-aot`). Blazor AOT was not re-measured; an earlier run had
it at 142.3 ms for create 1,000 rows and 1,400 ms startup.

| Case                         | NetWasm.UI Oz | NetWasm.UI O3 | SolidJS |  React | Blazor |
|------------------------------|--------------:|--------------:|--------:|-------:|-------:|
| create 1,000 rows            |          41.6 |          40.2 |    37.7 |   52.0 |  174.8 |
| replace 1,000 rows           |          45.8 |          44.1 |    40.7 |   57.8 |  198.1 |
| partial update (every 10th)  |          16.8 |          16.3 |    18.5 |   22.0 |   61.1 |
| select row                   |           2.8 |           3.1 |     2.3 |    8.7 |   59.1 |
| swap rows                    |           5.2 |           5.2 |     5.0 |   39.1 |   45.3 |
| remove row                   |           4.5 |           4.1 |     4.7 |    6.9 |   49.8 |
| create 10,000 rows           |         474.9 |         457.1 |   394.9 |  858.4 | 1641.7 |
| append 1,000 rows            |          52.7 |          55.5 |    43.2 |   66.0 |  237.7 |
| clear 1,000 rows             |           9.3 |           8.8 |     7.3 |   12.7 |   41.5 |
| reactive: deep (100-chain)   |           9.1 |           7.7 |    17.3 |      – |      – |
| reactive: broad (1,000 fan-out) |        27.0 |          18.5 |    32.7 |      – |      – |
| reactive: diamond            |           2.1 |           1.5 |     4.1 |      – |      – |
| reactive: create/dispose 10k |           8.3 |           5.0 |    14.1 |      – |      – |
| sort 100k strings (no UI)    |          47.5 |          45.2 |   105.0 |      – |  253.8 |
| sieve 2M (no UI)             |           5.8 |           6.8 |    55.5 |      – |   32.6 |
| startup (`__ready`)          |          98.4 |         104.3 |    20.4 |   45.8 |  893.0 |
| download, Brotli (KB)        |         113.7 |         131.4 |     6.2 |   58.4 | 2087.3 |

Script time alone (without layout), Oz vs. Solid: create 1,000 rows 12.5 vs 7.5, create 10,000
rows 168.6 vs 83.4, append 21.0 vs 11.7, replace 20.0 vs 14.8.

In short: NetWasm.UI is faster than React on every DOM case, 0.9–1.3× Solid's
time on every DOM case (faster at partial update and remove), faster than Solid on every
reactive and compute workload, and 3–20× faster than Blazor (interpreter) with a ~18× smaller
download and ~9× faster startup. The two runs agreed within 0.1× of Solid on every DOM case.
The pure-compute rows vary between runs because Chromium tiers Wasm and JS up at different
times (Liftoff → TurboFan).

### NetWasmGC vs. Compact

Earlier runs (NetWasm.Sdk 0.13.0, NetWasm.UI 0.9.0) with `--gc NetWasm,Compact` built the Oz
app with each collector the same way. Script time in ms, mean of two runs; NetWasmGC is the
default for the template, samples and benchmark since 0.9.0:

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

- Creating many rows costs 1.1–1.2× Solid with layout and up to 2× in script time, growing
  with the list size. The browser's DOM work is the same as Solid's; the gap is per-row work in
  C# and in the batch-applying JavaScript (see the profile in
  [#2](https://github.com/matengo/NetWasm.UI/issues/2)). 0.9.0 removed the `params` array in
  `With` and the `Map` lookup in `nwui.dom`. Since then, integer text holes go to the host as
  numbers (`SetTextInt`) and clearing or replacing a list removes its rows with one op
  (`RemoveRange`; clear 1,000 rows 10.1 → 9.1 ms script).
- Where the time for create 10,000 rows goes, measured warm in the benchmark app (Oz, about
  34 ms in total, see [#8](https://github.com/matengo/NetWasm.UI/issues/8)): ~22 ms in the
  JavaScript that applies the batch, almost all of it the browser's own `cloneNode` and
  `insertBefore`, and ~12 ms in Wasm. Of the Wasm part, building the data, the list and the
  clones takes ~5 ms, the class and text bindings ~2 ms and the two click listeners per row ~4 ms.
  Prototypes of the remaining ideas each saved 1 ms or less, below the run-to-run noise, so they
  were not merged: handlers that take data instead of a closure (0.2 ms), an array instead of a
  dictionary for the handler table (1 ms), and generated per-template walkers (the hole walk is
  ~3 ms in total and a walker saves at most 1 ms). Removing 10,000 laid-out rows takes the
  browser ~33 ms on its own, so clearing a large list is now at that floor.
- `O3` is faster than `Oz` on some reactive work but not on most DOM cases, and grows the total
  Brotli download by ~12 % (114 → 128 KB). `Oz` stays the default.
- Startup includes downloading and compiling the Wasm (~470 KB raw download in total vs. 17 KB of
  JS for Solid). On a local server the download and compile take only a few ms. Most of the time
  goes to the serial module chain before the Wasm request, which
  [startup preloading](deployment.md#startup-preloading) removes on a real network.
