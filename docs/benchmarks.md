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

Windows arm64 laptop, Chromium 153, 10 runs, script + layout, after template cloning and event
delegation:

| Case                         | NetWasm.UI Oz | NetWasm.UI O3 | SolidJS |  React | Blazor | Blazor AOT |
|------------------------------|--------------:|--------------:|--------:|-------:|-------:|-----------:|
| create 1,000 rows            |          79.0 |          78.8 |    55.7 |   72.4 |  391.5 |      142.3 |
| replace 1,000 rows           |          74.1 |          70.7 |    53.8 |   96.3 |  278.0 |      168.5 |
| partial update (every 10th)  |          21.3 |          23.2 |    23.5 |   34.7 |   88.7 |       44.5 |
| select row                   |           3.9 |           4.3 |     5.7 |   20.2 |  100.7 |       47.8 |
| swap rows                    |           8.7 |          14.9 |     6.3 |   50.1 |   73.6 |       21.9 |
| remove row                   |           7.8 |           8.9 |     6.4 |   10.5 |   80.3 |       26.0 |
| create 10,000 rows           |         773.8 |         746.8 |   505.7 | 1030.7 | 2246.8 |     1612.2 |
| append 1,000 rows            |         100.2 |          86.3 |    54.3 |   78.6 |  294.4 |      223.3 |
| clear 1,000 rows             |          12.5 |          13.8 |    11.8 |   17.2 |   76.8 |       25.7 |
| reactive: deep (100-chain)   |          34.1 |          17.3 |    18.7 |      – |      – |          – |
| reactive: broad (1,000 fan-out) |       67.6 |          36.5 |    36.4 |      – |      – |          – |
| reactive: diamond            |          19.6 |           9.9 |     5.3 |      – |      – |          – |
| reactive: create/dispose 10k |          49.9 |          36.1 |    15.2 |      – |      – |          – |
| sort 100k strings (no UI)    |         247.0 |         219.4 |   132.7 |      – |  653.4 |      105.8 |
| sieve 2M (no UI)             |          25.8 |         185.8 |   138.2 |      – |   68.8 |       85.3 |
| startup (`__ready`)          |         156.9 |         166.0 |    27.3 |   60.2 | 1139.3 |     1400.1 |
| download, Brotli (KB)        |         123.9 |         141.7 |     6.2 |   58.4 | 1442.0 |     3162.4 |

Before template cloning (same machine, earlier run): create 1,000 rows 95.2, replace 106.1,
create 10,000 rows 1050.4, append 110.2, select 11.3, clear 15.8. Script time alone for
creating 1,000 rows dropped from ~45 to ~26–39 ms; the op batch shrank from ~440 KB to ~95 KB.
Cases move by ±10 % between runs (create 1,000 rows measured 69.2 and 79.0 in two runs).

In short: NetWasm.UI is now level with or faster than React on every DOM case except append,
faster than Solid at select and partial update, 1.1–1.8× Solid elsewhere, and 2–10× faster than
Blazor with a ~12× smaller download and ~7× faster startup. The pure-compute rows (`sort`,
`sieve`) vary a lot between runs because Chromium tiers Wasm and JS up at different times
(Liftoff → TurboFan); treat them as "same ballpark".

The fixes made after the first run are listed in the [CHANGELOG](../CHANGELOG.md).

## Still open

- Creating rows still costs ~1.4× Solid with layout and ~3× in script time. The remaining cost is
  in Wasm: per row a closure, a `TemplateInstance`, node handles and one effect per reactive
  binding. Fewer allocations per row (e.g. effects without closures) are the next step.
- `Computed` recomputes eagerly on every dirty notification, so a diamond recomputes its join
  once per branch (`diamond`). A push-pull model (mark stale, recompute on read) would fix it.
- `O3` is ~1.5–2× faster than `Oz` on reactive work but makes the Wasm bigger. `Oz` stays the
  default.
- Startup includes downloading and compiling ~360 KB of Wasm vs. 17 KB of JS.
