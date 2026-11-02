# Changelog

NetWasm.UI has no releases yet. This lists what has been built so far.

## Unreleased

### Core

- Signals (`Signal<T>`, `Computed<T>`, `Effect`, `Selector<TKey>`), a batching `Scheduler` and
  `Scope` ownership.
- Build-once components and the `Html` builder API: elements, static and reactive attributes,
  properties, events, `Bind`, `Spread`, `When`, keyed `Each` with a minimal-move diff.
- Binary render protocol: one `applyBatch` call per update, decoded by `dom.mjs`.
- Structured event payloads (`DomEvent`: key, value, checked, modifiers, button, coordinates).
- Client-side router: `Routes`, `Route` patterns with `{id}` and `{*rest}`, `Link`, `NavLink`,
  `MemoryHistory` for tests.
- Context and stores (`Context<T>`, `Provide`, `Use`) and lifecycle hooks (`OnMount`,
  `OnCleanup`).
- Error handling: `ErrorBoundary` with a fallback and `Reset()`, and a global `App.OnError`
  hook. Errors from builds, effects, event handlers and `OnMount` go to the nearest boundary.
  Unhandled errors are rethrown after the DOM update has been applied.

### Templates

- `.nwui` templates compiled by a Roslyn source generator: `@expr`, attributes, events,
  `@bind`, `@ref`, `@if`/`else`, keyed `@foreach`, components, `@code`, `#line` mapping for
  C# errors.
- Child content and named slots (`Slot`, `@render`, `<template slot="…">`).
- `<ErrorBoundary>` with `<template slot="Fallback" context="error">`.
- Reactive props: `@prop` and `Prop<T>`.
- VS Code extension with syntax highlighting (`tools/vscode-nwui`).

### Tooling

- Kestrel dev server (replacing a Python server): Brotli/gzip, correct MIME types, SPA fallback,
  live reload over WebSocket, build error overlay, content-hash change detection, `O1` dev
  builds, `no-cache` with 304s.
- Playwright E2E tests and GitHub Actions CI with a size report.
- Benchmark against SolidJS, React and Blazor WebAssembly (`benchmarks/`).

### Size

- `Counter.wasm` reduced from 645 KB to 463 KB (102 → 77 KB Brotli): no interpolation with
  number holes, no `IFormattable`, a non-generic keyed-list diff, own URL encoding, no multicast
  delegates.

### Performance (fixes found by the benchmark)

First run (Oz, script + layout) before the fixes: select row 40.8 ms, clear 119.9 ms, replace
213.8 ms, create 10,000 rows 2369.6 ms, reactive `broad` 3509.2 ms. After: 11.3, 15.8, 106.1,
1050.4 and 65.7 ms.

- Signals kept dependents in a `List` (`Contains` on subscribe, `Remove` on re-track), so many
  readers of one signal were O(n²). Links are now index-based (O(1)), and `Selector<TKey>` was
  added for selection state.
- `dom.mjs` scanned every listener for each released node; it now keeps a per-node handler list.
- `Each` row scopes were registered with (and never removed from) the owner scope.
- Reactive attributes emitted an op on every re-run; they now skip unchanged values.

### Performance (template cloning and event delegation)

- `Template`: static HTML with holes (`<tag $>`, `<!--$-->`, `<!--@-->`) that the browser parses
  once and clones. New ops `DefineTemplate` and `CloneTemplate`; appending to a comment inserts
  before it, so any content can go into an insertion point.
- The `.nwui` generator turns every native element subtree into a static template; only bound
  attributes, handlers, `@expr` text and components/blocks are set per instance.
- Common bubbling events are delegated to one `document` listener per type.
- The benchmark row uses a `Template`. Creating 1,000 rows sends ~95 KB of ops instead of
  ~440 KB. Script + layout (Oz): create 1,000 rows 95.2 → 79.0 ms, replace 106.1 → 74.1,
  create 10,000 rows 1050.4 → 773.8, select 11.3 → 3.9, clear 15.8 → 12.5. The Wasm grows by
  ~5 KB raw (~1 KB Brotli) for the Counter sample.
