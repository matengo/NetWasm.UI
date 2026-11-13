# Architecture

## Why not a Blazor-style render tree?

NetWasm is a closed-world AOT toolchain: no reflection, no `System.Linq`, no runtime code
generation, and a small output. Blazor-style render trees and reflection-driven binding do not
fit that model. NetWasm.UI instead uses:

- **Signals** (`Signal<T>`, `Computed<T>`, `Effect`) with automatic dependency tracking.
- **Build-once components.** `Build()` runs exactly once; reactive expressions (`Func<T>` or
  signals) create targeted effects on single text nodes, attributes or properties.
- **A binary op batch.** All DOM mutations in a tick are encoded into one `byte[]` and applied by
  a small JS decoder (`dom.mjs`) in a single call.
- **One event entry point.** Events flow back through one JS→Wasm callback
  (`dispatch(handlerId, payload)`) carrying a small binary `DomEvent`. The handler runs in a
  scheduler batch, so a click that writes ten signals still yields one `applyBatch`.

The result: every state change crosses the Wasm→JS boundary **at most once**.

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

## Projects

| Project | Role |
|---|---|
| `src/NetWasm.UI` | Reactivity, renderer, components, `Html` API, router. Targets `netwasm0.1` and `net10.0` (for tests). Has no browser dependency. |
| `src/NetWasm.UI.Browser` | `[JSImport]`/`[JSExport]` bindings, `BrowserApp`, browser history and `dom.mjs`. `netwasm0.1` only. |
| `src/NetWasm.UI.Generator` | Roslyn incremental source generator for `.nwui` templates (`netstandard2.0`). |

The core talks to the browser through `IDomHost` (`ApplyBatch`), so unit tests run on `net10.0`
against `FakeDomHost`, an in-memory decoder of the same protocol.

## Reactivity internals

- A `Signal<T>` keeps its subscribers in two-way links that remember their index on both sides
  (as in Solid), so subscribing and unsubscribing are O(1) even with thousands of readers.
- Propagation is push-pull. A changed signal marks its observers Dirty and everything further
  down Check without running anything. Before a computation runs it refreshes its sources in
  order; it re-runs only if one of them changed, otherwise it goes back to Clean. A `Computed<T>`
  recomputes on read and only propagates if its value changed. A `Selector` stays eager.
- `Effect` re-runs are queued on the `Scheduler`. At batch depth 0 the queue is flushed and the
  `Renderer` ships one batch to the host; `OnMount` callbacks run after that, in their own batch.
- A `Scope` owns disposables (effects, nested scopes, components). Scopes form a tree that
  context lookups walk. `Each` rows get scopes detached from the owner (the list disposes them),
  so removed rows do not accumulate in the parent scope.
- Keyed lists (`Each`) diff old and new keys with a longest-increasing-subsequence pass and emit
  the minimum number of `InsertBefore` ops. The diff is non-generic (`KeyedList` over boxed keys)
  to keep the Wasm small.

## Render protocol

Opcode `u8`, integers little-endian `i32`, strings `u32` byte length + UTF‑8, booleans `u8`.
Defined in `src/NetWasm.UI/Dom/Op.cs`; the decoders in `dom.mjs` and `FakeDomHost.cs` mirror it.

| Op | Operands | Note |
|----|----------|------|
| CreateElement / CreateText / CreateAnchor | id, tag / text / – | anchors are comment nodes used as insertion markers |
| SetText, SetAttribute, RemoveAttribute, SetProperty, SetBoolProperty | id, … | |
| AppendChild, InsertBefore | parent/reference, child | `InsertBefore` uses `reference.parentNode`; appending to a comment inserts before it (template insertion points) |
| Remove / Release | id | `Remove` detaches but keeps the handle; `Release` drops it and its listeners |
| AddListener / RemoveListener | id, event, handlerId, flags | flags: 1 = preventDefault, 2 = stopPropagation, 4 = preventDefault only for a plain primary click |
| Mount | id, selector | `document.querySelector(selector).appendChild`; `replaceChildren` if the target has `data-nwui-prerender` |
| Focus | id | |
| DefineTemplate | templateId, html | parses the HTML into a `<template>` once and records the paths to its holes |
| CloneTemplate | templateId, firstId | clones it; the root gets `firstId`, hole *i* gets `firstId + i` |

Node ids are allocated in C#; `dom.mjs` keeps an id → node table and a per-node list of handlers
so releasing a node also drops its listeners.

## Static templates

`Template` (in `Template.cs`) scans its HTML for holes in C# without parsing it into nodes:
the root element, elements with a bare `$` attribute, `<!--$-->` (becomes an empty text node) and
`<!--@-->` (stays a comment and is used as an insertion point). `dom.mjs` finds the same holes
in the parsed `<template>`, so both sides agree on the numbering. A template is defined lazily
the first time it is cloned in a renderer generation; `Clone()` then reserves consecutive ids
and emits one `CloneTemplate` op. The generator turns every native element subtree of a
`.nwui` file into a static template, so only bound attributes, handlers, `@expr` text and
components/blocks cost ops per instance.

## Event delegation

Bubbling events in a fixed set (`click`, `dblclick`, `input`, `change`, `submit`, `keydown`,
`keyup`, pointer/mouse up/down, `focusin`/`focusout`, `contextmenu`, `beforeinput`) are not
attached per element. `AddListener` stores `[handlerId, flags]` on the node (`node.$$nw[type]`)
and `dom.mjs` adds one listener per type on `document` the first time it is
needed. On an event it walks `parentNode` from the target, applies each handler's flags, builds
the payload once and calls `dispatch`; `stopPropagation` ends the walk. Other events use a
direct listener. Delegated handlers run after native listeners on ancestors.

## Event payload

JS → Wasm, decoded by `DomEvent.Decode`:

```
u8 flags, i32 button, i32 clientX, i32 clientY, str key, str value
flags = ctrl 1 | shift 2 | alt 4 | meta 8 | checked 16 | repeat 32
```

An empty payload means "plain event, no data". `DomEvent.Encode()` is the C# reference encoder
used by tests; `payloadFor` in `dom.mjs` is the production encoder.

## Templates

The generator parses `.nwui` into a small tree (elements, text, `@expr`, `@if`, `@foreach`,
components, slots) and emits `Template`/`Html`/`TemplateRuntime` calls into a `partial class` with a
`Build()` override. Native elements become static templates (see above). User C# is emitted under `#line` directives so compiler errors point at the
template. See [Templates](templates.md).

## Prerendering

The user-facing side is in [Prerendering](deployment.md#prerendering).
`NetWasm.UI.Prerender.targets` runs after `Publish` and after the preload injection. It calls
`build/prerender.mjs` in Node once for all routes.

1. **Parent process.** It reads the published page, removes any earlier
   `<!--nwui:prerender-->…<!--/nwui:prerender-->` block, and writes `--shell` if requested. It then
   runs one child process per route (`spawnSync`). Each route needs its own process, because the
   bundled `nwui.dom` module keeps its default host in a module-level variable. A route that fails
   prints `NetWasm.UI prerender warning: …` and exits with code 0. MSBuild reports that line as a
   warning and leaves the page as it was.
2. **Child process.** It enables Wasm exception handling (`--experimental-wasm-exnref`) if Node
   needs it, and adds the browser APIs that the NetWasm loader uses: `fetch` for `file:` URLs,
   `Blob`/`URL.createObjectURL` as `data:` URLs, and a stub `document` with `title`, `baseURI`
   and `querySelector`. It sets `globalThis.__nwuiPrerenderHost` to an in-memory DOM host, which
   `dom.mjs` uses in place of `createDomHost()`. The host reports the route, under the base path,
   as the current location, so the router renders that page. Then the child imports the app
   module and calls the start export.
3. **In-memory DOM.** The host decodes the same [render protocol](#render-protocol) as `dom.mjs`,
   into a tree of plain objects. `DefineTemplate` parses the template HTML with a small
   parser, and finds the holes by the same rules as the browser. Listeners are ignored.
   Properties such as `value`, `checked` and `disabled` become attributes, so they appear in the
   HTML.
4. **Snapshot.** The child waits until no batch has arrived for 100 ms, for at most 3 s. That
   lets timers, resolved promises and local fetches render. It then serializes the mounted
   children and the last `document.title`, and prints them as JSON.
5. **Injection.** The parent writes the HTML between the markers inside the mount element. It
   adds `data-nwui-prerender` to the element, and replaces `<title>`. Mount then replaces the
   prerendered children instead of appending to them.

## Globals

`Scheduler`, `Renderer`, the dependency tracker and `Router.Current` are static. That keeps the
code small and fast, but it means one app per Wasm module instance.
