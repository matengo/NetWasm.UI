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

Windows arm64 laptop, Chromium 153, 10 runs, script + layout, NetWasm.Sdk 0.10.7 / NetWasm.UI 0.1.0
(zero-copy batch, per-instance DOM host, Trusted Types). Blazor AOT was not re-measured
(`--no-aot`); the previous run had it at 142.3 ms for create 1,000 rows and 1,400 ms startup.

| Case                         | NetWasm.UI Oz | NetWasm.UI O3 | SolidJS |  React | Blazor |
|------------------------------|--------------:|--------------:|--------:|-------:|-------:|
| create 1,000 rows            |          66.6 |          57.0 |    47.5 |   74.4 |  339.0 |
| replace 1,000 rows           |          72.0 |          72.4 |    61.8 |   81.2 |  407.9 |
| partial update (every 10th)  |          22.4 |          24.4 |    22.9 |   31.3 |   86.5 |
| select row                   |           3.8 |           4.7 |     2.9 |   11.9 |   94.1 |
| swap rows                    |           7.4 |           7.3 |     6.1 |   65.1 |   68.1 |
| remove row                   |           5.1 |           5.2 |     6.5 |    9.4 |   79.5 |
| create 10,000 rows           |         670.6 |         647.8 |   495.2 |  986.4 | 2208.6 |
| append 1,000 rows            |         101.1 |          94.5 |    55.2 |   84.0 |  302.6 |
| clear 1,000 rows             |          14.0 |          17.4 |    12.4 |   19.0 |   90.1 |
| reactive: deep (100-chain)   |          10.9 |           8.2 |    21.3 |      – |      – |
| reactive: broad (1,000 fan-out) |       28.5 |          21.0 |    40.4 |      – |      – |
| reactive: diamond            |           7.8 |           5.7 |     5.6 |      – |      – |
| reactive: create/dispose 10k |          16.1 |          13.5 |    16.4 |      – |      – |
| sort 100k strings (no UI)    |          94.5 |          96.8 |   156.1 |      – |  721.6 |
| sieve 2M (no UI)             |           9.6 |          10.3 |   167.5 |      – |   47.9 |
| startup (`__ready`)          |         147.1 |         159.7 |    30.4 |   61.2 | 1182.8 |
| download, Brotli (KB)        |         116.9 |         129.6 |     6.2 |   58.4 | 1442.2 |

A second run of the same build gave append 102.6, create 10,000 rows 665.9 and startup 133.1
(Oz). Small cases (select, swap, remove, clear) move by a few ms between runs.

Compared with the previous Oz run (NetWasm.Sdk 0.10.3): select, swap, remove, clear and partial
update are 10–50 % faster, all reactive workloads are 15–25 % faster, `sort` is 25 % faster and
`sieve` is ~8× faster (0.10.7 adds Wasm SIMD and a loop vectorizer). Download dropped from 123.2
to 116.9 KB Brotli. Append 1,000 rows (~101 vs 83.4) and startup (~133–147 vs 118.9) looked
slower, but an A/B run of the previous commit on the same day measured 105.8 and 123.6, so append
is unchanged and startup is within run-to-run noise (Solid moved 25–30 ms between the same runs).
O3 is now level with or faster than Oz on the DOM cases.

In short: NetWasm.UI is faster than React on every DOM case except append, level with or faster
than Solid at partial update, remove and all reactive workloads except diamond, 1.1–1.8× Solid
elsewhere, and 3–25× faster than Blazor (interpreter) with a ~12× smaller download and ~8×
faster startup.
The pure-compute rows vary between runs because Chromium tiers Wasm and JS up at different
times (Liftoff → TurboFan).

The fixes made after earlier runs are listed in the [CHANGELOG](../CHANGELOG.md).

## Still open

- Creating rows still costs ~1.4× Solid with layout and ~2.5–3× in script time. Per row there are
  still the row's closures, a `TemplateInstance` with node handles, one binding per reactive hole,
  two listeners, a scope and a boxed key.
- `Computed` recomputes eagerly on every dirty notification, so a diamond recomputes its join
  once per branch (`diamond`). A push-pull model (mark stale, recompute on read) would fix it.
- `O3` is ~1.5–2× faster than `Oz` on reactive work but makes the Wasm bigger. `Oz` stays the
  default.
- Startup includes downloading and compiling ~360 KB of Wasm vs. 17 KB of JS.
