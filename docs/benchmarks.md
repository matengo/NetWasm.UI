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
wasmtime run benchmarks/WasmBench/bin/Release/net10.0-netwasm/publish/WasmBench.wasm       # all cases
wasmtime run benchmarks/WasmBench/bin/Release/net10.0-netwasm/publish/WasmBench.wasm create 50  # filter, runs
```

It runs the cases of the browser runner from the same starting state. Each line is the case, the
median and the fastest time in microseconds, the time of the first run, and per run the bytes
allocated, the collections and the bytes of render batch. Use it for changes to the reactive core,
the renderer and the template compiler; changes to `dom.mjs` still need the browser.

## Method

- Every DOM case opens a fresh page, waits until it has made no request for 500 ms, runs the setup
  clicks, forces a GC and then times one `element.click()` until the DOM check passes (row count,
  labels, selected row). The wait and the GC are the ones js-framework-benchmark uses: it loads
  each page with `waitUntil: "networkidle0"` before its warm-up, and forces a last-resort major GC
  before each measured run.
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
- *Startup* is `window.__ready`, the page's own `performance.now()` once the app has started. The
  runner reads it after the same network-idle wait, so nothing of the runner's runs in the page
  while the app is starting. Polling for `__ready` from the load event on added about 5 ms to
  NetWasm.UI's startup, whose module chain is still running at that point.
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
NetWasm.UI after 0.14.0 (with smaller rows and the render buffer change). Each value is
the mean of the medians of two runs with 20 runs per case (`--runs 20 --no-aot`). Blazor AOT was
not re-measured; an earlier run had it at 142.3 ms for create 1,000 rows and 1,400 ms startup.

These runs force the same last-resort GC before the measured click as js-framework-benchmark,
which makes most cases slower for every app than the plain GC of earlier runs (Solid's create
1,000 rows went from 37.7 to 44.9 ms), so compare the columns with each other rather than with
older tables.

| Case                         | NetWasm.UI Oz | NetWasm.UI O3 | SolidJS |  React | Blazor |
|------------------------------|--------------:|--------------:|--------:|-------:|-------:|
| create 1,000 rows            |          43.2 |          42.0 |    43.2 |   60.8 |  200.2 |
| replace 1,000 rows           |          50.1 |          47.3 |    50.9 |   74.1 |  236.7 |
| partial update (every 10th)  |          23.1 |          22.6 |    24.2 |   31.8 |   78.9 |
| select row                   |           1.7 |           1.6 |     1.7 |    7.7 |   57.0 |
| swap rows                    |           5.8 |           4.9 |     5.9 |   45.9 |   54.7 |
| remove row                   |           5.6 |           4.8 |     5.7 |   10.6 |   59.1 |
| create 10,000 rows           |         425.9 |         407.1 |   389.8 |  582.0 | 1366.1 |
| append 1,000 rows            |          55.7 |          54.3 |    50.4 |   70.1 |  207.9 |
| clear 1,000 rows             |           8.5 |           8.8 |     8.3 |   15.6 |   47.7 |
| reactive: deep (100-chain)   |           9.3 |           6.9 |    17.9 |      – |      – |
| reactive: broad (1,000 fan-out) |        27.8 |          17.1 |    32.2 |      – |      – |
| reactive: diamond            |           2.4 |           1.6 |     5.0 |      – |      – |
| reactive: create/dispose 10k |           7.8 |           4.8 |    14.4 |      – |      – |
| sort 100k strings (no UI)    |          48.7 |          44.0 |   104.4 |      – |  261.1 |
| sieve 2M (no UI)             |           5.8 |           6.7 |    55.5 |      – |   32.5 |
| startup (`__ready`)          |          98.3 |         106.1 |    21.3 |   47.6 |  911.3 |
| download, Brotli (KB)        |         119.2 |         138.0 |     6.2 |   58.4 | 2087.4 |

Script time alone (without layout), Oz vs. Solid: create 1,000 rows 10.6 vs 11.3, create 10,000
rows 103.0 vs 87.7, append 14.3 vs 13.5, replace 16.8 vs 18.4, partial update 2.8 vs 4.1.

In short: NetWasm.UI is faster than React on every DOM case, 0.95–1.11× Solid's time on every
DOM case (level at create 1,000 rows, select row and clear, faster at replace, partial update,
swap and remove), faster than Solid on every reactive and compute workload, and 3–30× faster than Blazor
(interpreter) with a ~18× smaller download and ~9× faster startup. The two runs agreed within
about 0.1× of Solid on every DOM case except select row, where 0.2 ms of a 1.7 ms case is 12 %.
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
