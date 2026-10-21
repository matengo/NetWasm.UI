# NetWasm.UI

A reflection-free, signal-driven C# UI framework for the browser, compiled ahead-of-time to
WebAssembly with [NetWasm](https://www.netwasm.com). Inspired by Solid and Svelte rather than
Blazor: components build their DOM once, fine-grained signals patch exactly the nodes that
changed, and every state change crosses the Wasm→JS boundary **at most once**.

> Status: proof of concept. The core works end-to-end in the browser (see `samples/Counter`),
> but the API will change.

## Why

NetWasm is a closed-world AOT toolchain: no reflection, no `System.Linq`, no runtime code
generation — and a tiny output. Blazor-style render trees and reflection-driven binding do not
fit that model. NetWasm.UI instead uses:

- **Signals** (`Signal<T>`, `Computed<T>`, `Effect`) with automatic dependency tracking.
- **Build-once components** – `Build()` runs exactly once; reactive expressions
  (`Func<T>` / signals) create targeted effects on single text nodes, attributes or properties.
- **A binary op batch** – all DOM mutations in a tick are encoded into one `byte[]` and applied
  by a ~200-line JS decoder (`dom.mjs`) in a single call.
- **Events** flow back through one JS→Wasm callback (`dispatch(handlerId, payload)`), wrapped in
  a scheduler batch, so a click that touches ten signals still yields one `applyBatch`.

### Sample sizes (Counter + TodoList demo, Release)

| Artifact                | Raw     | Brotli  | Gzip    |
|-------------------------|---------|---------|---------|
| `Counter.wasm`          | 316 KB  | 59 KB   | 75 KB   |
| `netwasm.page.mjs` host | 315 KB  | 59 KB   | 70 KB   |
| **Whole page (10 requests)** | **649 KB** | **125 KB** | – |

Half of the download is NetWasm's own JS host, not application code. The dev server
(`tools/DevServer`) compresses with Brotli/gzip so DevTools "transferred" numbers resemble a
production server.

## Quick look

```csharp
using NetWasm.UI;
using NetWasm.UI.Reactive;
using static NetWasm.UI.Html;

public sealed class CounterView : Component
{
    private readonly Signal<int> count = new(0);

    protected override Node Build()
    {
        var doubled = new Computed<int>(() => count.Value * 2);

        return Section(
            H2("Counter"),
            P("Count: ", Strong(Text(count)), " · doubled: ", Text(doubled)),
            Button(OnClick(() => count.Value++), "+1"),
            Button(Disabled(() => count.Value == 0), OnClick(() => count.Value = 0), "Reset"),
            When(() => count.Value >= 10, () => P("That's a lot of clicks!")));
    }
}
```

Lists are keyed and reconciled with a longest-increasing-subsequence diff, so reorders emit the
minimum number of `insertBefore` ops:

```csharp
Ul(Each(items, i => i.Id, item =>
    Li(
        Class(() => item.Done.Value ? "todo done" : "todo"),
        Label(Input(Type("checkbox"), Spread(Bind(item.Done))), " ", item.Title),
        Button(OnClick(() => Remove(item)), "×"))))
```

## Repository layout

| Path                         | What                                                                 |
|------------------------------|----------------------------------------------------------------------|
| `src/NetWasm.UI`             | Core: reactivity, binary renderer, component/builder API. Targets `netwasm0.1` and `net10.0` (for tests). |
| `src/NetWasm.UI.Browser`     | Browser host: `[JSImport]` bindings + `dom.mjs` decoder. `netwasm0.1` only. |
| `samples/Counter`            | Counter + TodoList demo app (`OutputType=JsModule`, page host).       |
| `tests/NetWasm.UI.Tests`     | xunit tests running against an in-memory `FakeDomHost` decoder.       |
| `tools/DevServer`            | Kestrel dev server: publishes the app, serves it with Brotli/gzip and correct MIME types, watches sources and reloads the browser. |

## Getting started

Prerequisites: .NET SDK 10.0.3xx. The NetWasm SDK is resolved from `global.json` and downloads
its host tooling on first restore.

```powershell
dotnet test                                        # core unit tests (net10.0)
dotnet run --project tools/DevServer -- samples/Counter
# → publishes, serves http://localhost:8765/ and rebuilds + reloads the browser on save
```

Dev server options: `--port <n>`, `-O O0|O1|Oz` (NetWasm optimization, default `O1`),
`--release` (= `-O Oz`, production sizes), `-c Debug|Release`, `--no-watch` (serve only),
`--no-publish` (skip initial publish), `-w <dir>` (watch root, default repo root).

### Live reload, not hot reload

NetWasm is closed-world AOT: there is no runtime to patch, so .NET Hot Reload (Edit & Continue)
cannot apply. The dev server instead watches sources, re-runs `dotnet publish` and pushes a reload
over a WebSocket; build errors are shown as an overlay in the page. Saves that do not change file
content (editor touches, formatters) are ignored via content hashing. Preserving signal state
across reloads is on the roadmap.

#### Why `dotnet publish`, and how long it takes

The browser layout (`browser/*.mjs`, `netwasm.page.mjs`, `deployment.json`) is only produced by
the SDK's publish target, so a plain `build` is not enough. Measured on the Counter sample after a
one-line `.cs` change (NetWasm 0.10.0, Release):

| Step (MSBuild target)              | Oz (default) | Notes                                   |
|------------------------------------|-------------:|-----------------------------------------|
| `NetWasmCompileCoreModule`         |       ~7.2 s | AOT, scales with optimization level     |
| `NetWasmLinkRawModule`             |       ~5.9 s | wasm-ld + binaryen                      |
| `NetWasmSdkResolveBuildEnvironment`|       ~3.5 s | fixed: probes node/wasm-ld every run    |
| `NetWasmBuildRawBindings`          |       ~2.6 s |                                         |
| MSBuild + restore overhead         |       ~1.4 s |                                         |
| `NetWasmSdkPublishJsModule`        |       ~0.3 s | the actual "publish" part is negligible |
| **Total**                          |    **~21 s** |                                         |

| `NetWasmOptimization` | Publish after edit | `Counter.wasm` |
|-----------------------|-------------------:|---------------:|
| `Oz` (production)     |             ~21 s  |         316 KB |
| `O1` (dev default)    |             ~14 s  |         519 KB |
| `O0`                  |             ~12 s  |         615 KB |

`Debug` configuration is slower (~32 s) and larger (869 KB), so the dev server stays on Release.
The remaining floor is in the toolchain itself (environment probing, non-incremental AOT) and is
feedback for the NetWasm team rather than something this repo can shave further.

## Architecture

```mermaid
flowchart LR
    subgraph Wasm["C# (Wasm)"]
        S[Signal / Computed] --> E[Effect]
        E --> R[Renderer\nbinary op batch]
        SCH[Scheduler\nBatch / Flush] --> R
        D[Renderer.Dispatch] --> SCH
    end
    subgraph JS["dom.mjs"]
        AB[applyBatch] --> DOM[(DOM)]
        DOM -- events --> L[listener] --> CB[dispatch callback]
    end
    R -- "1 call: byte[]" --> AB
    CB -- "handlerId, payload" --> D
```

### Render protocol

Opcode `u8`, integers little-endian `i32`, strings `u32` byte length + UTF‑8, booleans `u8`.
Defined in `src/NetWasm.UI/Dom/Op.cs`; the decoders in `dom.mjs` and `FakeDomHost.cs` mirror it.

| Op | Operands | Note |
|----|----------|------|
| CreateElement / CreateText / CreateAnchor | id, tag / text / – | anchors are comment nodes used as insertion markers |
| SetText, SetAttribute, RemoveAttribute, SetProperty, SetBoolProperty | id, … | |
| AppendChild, InsertBefore | parent/reference, child | `InsertBefore` uses `reference.parentNode` |
| Remove / Release | id | `Remove` detaches but keeps the handle; `Release` drops it and its listeners |
| AddListener / RemoveListener | id, event, handlerId, flags | flags: 1 = preventDefault, 2 = stopPropagation |
| Mount | id, selector | `document.querySelector(selector).appendChild` |
| Focus | id | |

Event payload convention (JS side): `input`/`change` → `target.value` (checkbox →
`"true"/"false"`), key events → `event.key`, otherwise `""`.

### Reactivity

- `Signal<T>` stores a value and its subscribers. Writing a changed value marks subscribers dirty.
- `Computed<T>` recomputes eagerly and only propagates if its value changed.
- `Effect` re-runs are queued on the `Scheduler`; at batch depth 0 the queue is flushed, then the
  `Renderer` ships one batch to the host.
- `Scope` owns disposables (effects, nested scopes, components) so `Show`/`For` branches tear
  down cleanly.

## Known limitations

- NetWasm 0.10 (`netwasm0.1`) has no `System.Linq`, reflection or `async`. Write plain loops.
- Globals (`Scheduler`, `Renderer`, tracker) mean exactly one app per module instance.
- Event payloads are strings only; no `DomEvent` property bag yet (clientX, modifiers, …).
- No routing, no SSR/hydration, no Razor syntax – components are built with the C# `Html` API.

## Roadmap ideas

- Razor-like syntax via a source generator that emits `Html` builder calls (no runtime cost).
- Richer event payloads (structured binary payload instead of a single string).
- Microtask-coalesced batching across multiple JS→Wasm events in one frame.
- Router, `<Suspense>`-style async boundaries once NetWasm supports async interop.
- Wasm size work: strip unused `Html` helpers, measure per-feature cost.
- Dev loop: snapshot/restore signal state across live reloads, CSS swap without reload, faster
  incremental NetWasm publish.

## Development notes

- Serve over `http://localhost` with correct MIME types (`tools/DevServer`); `file://` does not
  work and browsers cache a wrong `.mjs` MIME type aggressively (hard reload after fixing).
- `[JSImport]` methods are `static extern`; a `subscribe` host function must return an object with
  `dispose()` for `JSSubscription`.
- The JS module is declared by the **app** project
  (`<NetWasmJavaScriptModule Include="…/dom.mjs" Module="netwasm.ui.dom" />`) and is bundled into
  `netwasm.page.mjs`.

## License

MIT – see [LICENSE](LICENSE).
