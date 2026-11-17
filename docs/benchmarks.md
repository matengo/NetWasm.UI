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

### The C# side alone, under Wasmtime

Browser timings move by a few percent from run to run, which hides a change that saves 1 ms per
10,000 rows. `benchmarks/WasmBench` is the same `BenchApp.nwui` as a console program for
[Wasmtime](https://wasmtime.dev): it renders into a host that only counts the bytes of each batch,
so no browser, DOM or JavaScript takes part, and it starts every run right after a collection. Its
times repeat within about 1 %, and the bytes allocated and the batch size are exact.

```powershell
dotnet publish benchmarks/WasmBench -c Release -p:NetWasmOptimization=Oz
wasmtime run benchmarks/WasmBench/bin/Release/netwasm0.1/publish/WasmBench.wasm            # all cases
wasmtime run benchmarks/WasmBench/bin/Release/netwasm0.1/publish/WasmBench.wasm create 50  # filter, runs
```

It runs the cases of the browser runner from the same starting state. Each line is the case, the
median and the fastest time in microseconds, the time of the first run, and per run the bytes
allocated, the collections and the bytes of render batch. Use it for changes to the reactive core,
the renderer and the template compiler; changes to `dom.mjs` still need the browser.

## Method

- Every DOM case opens a fresh page, runs the setup clicks, forces a GC and then times one
  `element.click()` until the DOM check passes (row count, labels, selected row). The GC is the
  same one js-framework-benchmark forces before each measured run (a last-resort major GC).
- The setup clicks are the warm-up that
  [js-framework-benchmark](https://github.com/krausest/js-framework-benchmark) gives the same case,
  so every case is measured warmed up. Create 10,000 rows, for example, follows five rounds of
  create 1,000 rows and clear.
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

Windows arm64 laptop on an otherwise idle machine in the "Best performance" power mode, mains
power, Chromium 153 from Playwright 1.63, script + layout, NetWasm.Sdk 0.14.2 with NetWasmGC,
NetWasm.UI after 0.13.0 (with shared row bindings and listeners, and `ReleaseBlock`). Each value is
the mean of the medians of two runs with 20 runs per case (`--runs 20 --no-aot`). Blazor AOT was
not re-measured; an earlier run had it at 142.3 ms for create 1,000 rows and 1,400 ms startup.

These runs force the same last-resort GC before the measured click as js-framework-benchmark,
which makes every app slower than the plain GC of earlier runs (Solid's create 1,000 rows went
from 37.7 to 44.9 ms), so compare the columns with each other rather than with older tables.

| Case                         | NetWasm.UI Oz | NetWasm.UI O3 | SolidJS |  React | Blazor |
|------------------------------|--------------:|--------------:|--------:|-------:|-------:|
| create 1,000 rows            |          45.0 |          43.0 |    44.9 |   63.7 |  211.1 |
| replace 1,000 rows           |          50.5 |          48.3 |    51.9 |   76.7 |  245.7 |
| partial update (every 10th)  |          24.6 |          23.5 |    24.7 |   32.8 |   83.2 |
| select row                   |           1.9 |           1.6 |     1.6 |    7.8 |   62.3 |
| swap rows                    |           5.7 |           5.0 |     5.9 |   49.1 |   58.6 |
| remove row                   |           5.5 |           4.7 |     5.6 |   10.7 |   64.6 |
| create 10,000 rows           |         437.9 |         423.2 |   410.9 |  605.4 | 1336.9 |
| append 1,000 rows            |          63.3 |          59.9 |    50.6 |   72.3 |  209.6 |
| clear 1,000 rows             |           8.5 |           8.6 |     8.9 |   16.0 |   48.2 |
| reactive: deep (100-chain)   |           9.0 |           7.5 |    17.7 |      – |      – |
| reactive: broad (1,000 fan-out) |        27.6 |          18.2 |    32.8 |      – |      – |
| reactive: diamond            |           2.2 |           1.5 |     4.2 |      – |      – |
| reactive: create/dispose 10k |           7.6 |           4.8 |    13.9 |      – |      – |
| sort 100k strings (no UI)    |          51.3 |          47.9 |   108.2 |      – |  263.9 |
| sieve 2M (no UI)             |           5.9 |           7.2 |    55.6 |      – |   32.8 |
| startup (`__ready`)          |         103.7 |         108.9 |    20.9 |   48.6 |  917.8 |
| download, Brotli (KB)        |         118.5 |         137.3 |     6.2 |   58.4 | 2087.3 |

Script time alone (without layout), Oz vs. Solid: create 1,000 rows 11.6 vs 11.6, create 10,000
rows 102.2 vs 90.0, append 25.1 vs 13.4, replace 17.2 vs 18.4, partial update 3.0 vs 3.7.

In short: NetWasm.UI is faster than React on every DOM case, 0.95–1.25× Solid's time on every
DOM case (level at create 1,000 rows and partial update, faster at replace, swap, remove and
clear), faster than Solid on every reactive and compute workload, and 3–30× faster than Blazor
(interpreter) with a ~18× smaller download and ~9× faster startup. The two runs agreed within
about 0.1× of Solid on every DOM case except select row, where 0.4 ms of a 2 ms case is 30 %.
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

- Large lists and appending still cost more than Solid: create 10,000 rows 1.07× and append
  1,000 rows 1.25× with layout, 1.1× and 1.9× in script time. Create 1,000 rows is level. The browser's DOM work is the
  same as Solid's; the gap is per-row work in
  C# and in the batch-applying JavaScript (see the profile in
  [#2](https://github.com/matengo/NetWasm.UI/issues/2)). 0.9.0 removed the `params` array in
  `With` and the `Map` lookup in `nwui.dom`. Since then, integer text holes go to the host as
  numbers (`SetTextInt`) and clearing or replacing a list removes its rows with one op
  (`RemoveRange`; clear 1,000 rows 10.1 → 9.1 ms script). 0.13.0 made the rows of an `@foreach`
  share their bindings and event listeners
  ([#13](https://github.com/matengo/NetWasm.UI/issues/13)), and after it one op releases the
  node ids of a cleared list and `nwui.dom` keeps its compiled code across the browser's GC.
- Where the time for create 10,000 rows went before 0.13.0, measured warm in the benchmark app
  (Oz, about 34 ms in total, see [#8](https://github.com/matengo/NetWasm.UI/issues/8)): ~22 ms in the
  JavaScript that applies the batch, almost all of it the browser's own `cloneNode` and
  `insertBefore`, and ~12 ms in Wasm. Of the Wasm part, building the data, the list and the
  clones takes ~5 ms, the class and text bindings ~2 ms and the two click listeners per row ~4 ms
  (0.13.0 replaced those with one listener per template).
  Prototypes of the remaining ideas each saved 1 ms or less, below the run-to-run noise, so they
  were not merged: handlers that take data instead of a closure (0.2 ms), an array instead of a
  dictionary for the handler table (1 ms), and generated per-template walkers (the hole walk is
  ~3 ms in total and a walker saves at most 1 ms). Removing 10,000 laid-out rows takes the
  browser ~33 ms on its own, so clearing a large list is now at that floor.
- `O3` is faster than `Oz` on some reactive work but not on most DOM cases, and grows the total
  Brotli download by ~16 % (119 → 137 KB). `Oz` stays the default.
- Startup includes downloading and compiling the Wasm (~460 KB raw download in total vs. 17 KB of
  JS for Solid). On a local server the download and compile take only a few ms. Most of the time
  goes to the serial module chain before the Wasm request, which
  [startup preloading](deployment.md#startup-preloading) removes on a real network.
