# NetWasm.UI

A reflection-free, signal-driven C# UI framework for the browser, compiled ahead-of-time to
WebAssembly with [NetWasm](https://www.netwasm.com). Inspired by Solid and Svelte rather than
Blazor: components build their DOM once, fine-grained signals patch exactly the nodes that
changed, and every state change crosses the Wasm→JS boundary **at most once**.

> **Status: proof of concept.** The core works end-to-end in the browser (see
> `samples/Counter`), but the API will change and there are no NuGet packages yet.

## Quick look

Write components as `.nwui` templates (HTML + Razor-style `@`), compiled at build time by a
source generator:

```razor
@* CounterView.nwui → public partial class CounterView : Component *@
<section>
    <h2>Counter</h2>
    <p>Count: <strong>@count</strong> &middot; doubled: @doubled</p>
    <button @onclick="() => count.Value++">+1</button>
    <button disabled="@(count.Value == 0)" @onclick="() => count.Value = 0">Reset</button>
    @if (count.Value >= 10) {
        <p class="hint">That's a lot of clicks!</p>
    }
</section>

@code {
    private readonly Signal<int> count = new(0);
    private Computed<int> doubled = null!;

    partial void OnInit() => doubled = new Computed<int>(() => count.Value * 2);
}
```

…or directly in C#, which is what the generator emits:

```csharp
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

## Size and speed

- The whole demo app (counter, todos, router, nested components) is **140 KB** with Brotli;
  about 40 % of that is NetWasm's JS host.
- In a [js-framework-benchmark-style comparison](docs/benchmarks.md) it is level with or faster
  than React on most cases, 1.1–1.8× SolidJS, and 2–10× faster than Blazor WebAssembly with a
  ~12× smaller download and ~7× faster startup.

## Try it

Prerequisite: .NET SDK 10.0.3xx. The NetWasm SDK is resolved from `global.json`.

```powershell
dotnet run --project tools/DevServer -- samples/Counter
# → publishes, serves http://localhost:8765/ and rebuilds + reloads the browser on save
```

## Documentation

- [Getting started](docs/getting-started.md): a new app from scratch and the dev server
- [Components and the C# API](docs/components.md)
- [Templates (`.nwui`)](docs/templates.md)
- [Reactivity](docs/reactivity.md): signals, batching, context, lifecycle
- [Error handling](docs/error-handling.md): `ErrorBoundary` and `App.OnError`
- [Routing](docs/routing.md)
- [Publishing and size](docs/deployment.md)
- [Architecture](docs/architecture.md)
- [Benchmarks](docs/benchmarks.md)
- [Troubleshooting and known limitations](docs/troubleshooting.md)

## Repository layout

| Path                         | What                                                                 |
|------------------------------|----------------------------------------------------------------------|
| `src/NetWasm.UI`             | Core: reactivity, binary renderer, component/builder API, router. Targets `netwasm0.1` and `net10.0` (for tests). |
| `src/NetWasm.UI.Browser`     | Browser host: `[JSImport]` bindings and the `dom.mjs` decoder. `netwasm0.1` only. |
| `src/NetWasm.UI.Generator`   | Roslyn source generator: `*.nwui` templates → `Component` partial classes. |
| `samples/Counter`            | Demo app: counter, todos, routing and `/components` (nested components, `.nwui` vs C#). |
| `tests/NetWasm.UI.Tests`     | xunit tests against an in-memory `FakeDomHost`.                      |
| `tests/NetWasm.UI.E2E`       | Playwright browser tests against `samples/Counter`.                  |
| `tools/DevServer`            | Kestrel dev server with Brotli, live reload and an error overlay.   |
| `tools/vscode-nwui`          | VS Code extension: `.nwui` syntax highlighting.                      |
| `benchmarks/`                | Comparison with SolidJS, React and Blazor WebAssembly.               |

## Known limitations

No `System.Linq`, reflection or `async` in `netwasm0.1`; one app per Wasm module; no
base-path routing or SSR yet; no hot reload (live reload in 15–20 s); `.nwui` has
syntax highlighting but no IntelliSense. See [Troubleshooting](docs/troubleshooting.md#known-limitations).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for building, tests, the dev server internals and the
roadmap, and [CHANGELOG.md](CHANGELOG.md) for what has changed.

## License

MIT – see [LICENSE](LICENSE).
