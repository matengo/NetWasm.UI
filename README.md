# NetWasm.UI

A reflection-free, signal-driven C# UI framework for the browser, compiled ahead-of-time to
WebAssembly with [NetWasm](https://www.netwasm.com). Inspired by Solid and Svelte rather than
Blazor: components build their DOM once, fine-grained signals patch exactly the nodes that
changed, and every state change crosses the Wasm→JS boundary **at most once**.

**Website and live examples: [matengo.github.io/NetWasm.UI](https://matengo.github.io/NetWasm.UI/)**,
itself a NetWasm.UI app ([source](site/)).

> **Status: 0.9, an early but usable release.** The packages are on NuGet and the framework is
> tested in Chromium, Firefox and WebKit. The public API may still change in 0.x releases; every
> change is listed in the [CHANGELOG](CHANGELOG.md). See
> [Compatibility and versioning](docs/compatibility.md).

## Get started in 30 seconds

You need the [.NET SDK 10.0.3xx or 11](https://dotnet.microsoft.com/download).

```shell
dotnet new install NwUi.Templates
dotnet new netwasm-ui -o MyApp
cd MyApp
dotnet nwui-dev            # → http://localhost:8765, rebuilds and reloads on save
```

The template brings:

- a router,
- a C# component,
- two `.nwui` pages,
- CSS,
- the `nwui-dev` dev server, as a local .NET tool.

Read [Getting started](docs/getting-started.md) next.

## Packages

All packages are on nuget.org and are released together with the same version number. Package IDs
start with `NwUi` because the `NetWasm.` prefix on nuget.org belongs to the NetWasm toolchain; the
namespaces are `NetWasm.UI`.

| Package | What it is | Install |
|---|---|---|
| [`NwUi.Browser`](https://www.nuget.org/packages/NwUi.Browser) | The package an app references: the browser host and the `nwui.dom` module. It brings in `NwUi`. | `dotnet add package NwUi.Browser` |
| [`NwUi`](https://www.nuget.org/packages/NwUi) | The core: signals, components, router, forms and the `.nwui` template compiler. | Comes with `NwUi.Browser`. |
| [`NwUi.Templates`](https://www.nuget.org/packages/NwUi.Templates) | `dotnet new netwasm-ui`. | `dotnet new install NwUi.Templates` |
| [`NwUi.DevServer`](https://www.nuget.org/packages/NwUi.DevServer) | `dotnet nwui-dev`: publishes, serves (Brotli, SPA fallback) and live-reloads. | In the template. Otherwise `dotnet tool install --local NwUi.DevServer`. |
| VS Code: *NetWasm.UI templates (.nwui)* | Syntax highlighting for `.nwui`. | See below. |

### VS Code extension

The extension is not on the Marketplace yet. Install it from a GitHub release:

1. Download `nwui.vsix` from the [latest release](https://github.com/matengo/NetWasm.UI/releases/latest).
2. Run `code --install-extension nwui.vsix`. Or, in VS Code, open the Extensions view, click
   **…** and choose **Install from VSIX…**.

To build it from source, see [tools/vscode-nwui](tools/vscode-nwui/README.md).

## Quick look

You write components as `.nwui` templates (HTML with Razor-style `@`). A source generator
compiles them at build time:

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

Prefer plain properties? `[Reactive]` puts a signal behind a `partial` property, in `@code` or
any C# class ([Reactive properties](docs/reactivity.md#reactive-properties)):

```razor
<button @onclick="() => Count++">Clicked @Count times</button>

@code {
    [Reactive] private partial int Count { get; set; }
}
```

You can also write components directly in C#, which is what the generator emits:

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

- **Size:** the whole demo app (counter, todos, router, forms, nested components) is about
  **140 KB** with Brotli, Wasm and JavaScript together. CI fails a pull request that grows it
  past its [size budget](size-budget.json).
- **Speed:** in a [js-framework-benchmark-style comparison](docs/benchmarks.md)
  it is level with or faster than React on every case, and between 0.8× and 1.6× SolidJS's
  time. Against Blazor WebAssembly (interpreter) it is 3–20× faster, with a ~18× smaller
  download and ~9× faster startup.
- **Startup:** `dotnet publish` adds preload tags for the runtime modules and the Wasm to
  `index.html`, so everything downloads in parallel instead of one import level at a time. On
  ordinary broadband the demo app renders in about 315 ms instead of 700 ms. See
  [Startup preloading](docs/deployment.md#startup-preloading). With
  `NetWasmUIPrerender=true`, the first screen of each listed page is rendered into its HTML at
  publish time, so content shows before the Wasm has loaded. See
  [Prerendering](docs/deployment.md#prerendering).

## Requirements

| | |
|---|---|
| .NET SDK | 10.0.3xx or 11.0.x (C# 15 needs 11). The NetWasm SDK is resolved from `global.json`. |
| NetWasm.Sdk | 0.13.0, target framework `netwasm0.1` |
| Browsers | Chrome/Edge 137+, Firefox 131+, Safari 18.4+ (needed for Wasm exception handling with `exnref`) |

**Licensing.** NetWasm.UI is MIT. The NetWasm compiler and tools use the
[NetWasm Community License](https://github.com/zion-sati/NetWasm):

- It is free for individuals, open source, education, and organisations with fewer than 250
  employees and under USD 10 M in revenue.
- Larger organisations need a sponsorship.
- NetWasm's CoreLib and runtime are MIT, and the apps you build can use any license.

Check the current terms before you adopt it.

## Try the samples

From a clone of this repository:

```powershell
dotnet run --project tools/DevServer -- samples/Counter   # counter, todos, routing, forms, components
dotnet run --project tools/DevServer -- samples/Fetch     # HttpClient + System.Text.Json with Suspense
dotnet run --project tools/DevServer -- samples/Widgets   # several apps on one page
dotnet run --project tools/DevServer -- samples/Interop   # a JS library wrapped in a component
dotnet run --project tools/DevServer -- site              # the project website
```

## Documentation

- [Getting started](docs/getting-started.md): the template, the dev server and a first component
- [Components and the C# API](docs/components.md)
- [Templates (`.nwui`)](docs/templates.md)
- [Reactivity](docs/reactivity.md): signals, batching, context, lifecycle
- [Services and DI](docs/services.md): `Services`, `Inject<T>()`, `@inject`
- [Async data and Suspense](docs/async.md): `Resource<T>`, `Suspense`, `HttpClient`
- [Forms and validation](docs/forms.md)
- [Routing](docs/routing.md): parameters, nested routes, base path
- [Error handling](docs/error-handling.md): `ErrorBoundary` and `App.OnError`
- [JavaScript interop](docs/js-interop.md): `[JSImport]`, callbacks, wrapping JS libraries
- [Embedding and several apps on one page](docs/embedding.md)
- [Publishing and size](docs/deployment.md)
- [Security](docs/security.md): Content Security Policy, Trusted Types, XSS
- [Compatibility and versioning](docs/compatibility.md)
- [Architecture](docs/architecture.md)
- [Benchmarks](docs/benchmarks.md)
- [Troubleshooting and known limitations](docs/troubleshooting.md)

## Repository layout

| Path                         | What                                                                 |
|------------------------------|----------------------------------------------------------------------|
| `src/NetWasm.UI`             | Core: reactivity, binary renderer, component/builder API, router, forms. Targets `netwasm0.1`, plus `net10.0` for tests. |
| `src/NetWasm.UI.Browser`     | Browser host: `[JSImport]` bindings and the `dom.mjs` decoder. `netwasm0.1` only. |
| `src/NetWasm.UI.Generator`   | Roslyn source generator: `*.nwui` templates → `Component` partial classes. Ships inside the `NwUi` package. |
| `samples/Counter`            | Demo app: counter, todos, routing, nested routes, forms and `/components`. |
| `samples/Fetch`              | `HttpClient` + `System.Text.Json` with `Resource` and `Suspense`.    |
| `samples/Widgets`            | Several independent apps (Wasm instances) on one page.              |
| `samples/Interop`            | `[JSImport]`, `JSObject`, callbacks and Promises: a JS library in a component. |
| `site/`                      | The project website, built with NetWasm.UI and published to GitHub Pages. |
| `templates/`                 | The `dotnet new netwasm-ui` template package.                        |
| `tests/NetWasm.UI.Tests`     | xunit tests against an in-memory `FakeDomHost`.                      |
| `tests/NetWasm.UI.E2E`       | Playwright browser tests against the samples (Chromium, Firefox, WebKit). |
| `tools/DevServer`            | `nwui-dev`: Kestrel dev server with Brotli, live reload and an error overlay. |
| `tools/size-report`          | Brotli/gzip size report and the budget check CI runs.                |
| `tools/vscode-nwui`          | VS Code extension: `.nwui` syntax highlighting.                      |
| `benchmarks/`                | Comparison with SolidJS, React and Blazor WebAssembly.               |

## Known limitations

- No reflection in `netwasm0.1`.
- No `System.Linq` in the core library. Apps may use the NetWasm LINQ package.
- One app per Wasm module.
- No server-side rendering (SSR) per request. Pages can be
  [prerendered](docs/deployment.md#prerendering) at publish time, but the app replaces that
  markup when it starts (no hydration yet), so handlers only work once the Wasm has loaded.
- No hot reload; live reload takes ~3–4 s and keeps simple signal values and form state.
- `.nwui` has syntax highlighting but no IntelliSense.

See [Troubleshooting](docs/troubleshooting.md#known-limitations).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for building, tests, the public API workflow and the
roadmap. See [CHANGELOG.md](CHANGELOG.md) for what has changed.

## License

MIT – see [LICENSE](LICENSE).
