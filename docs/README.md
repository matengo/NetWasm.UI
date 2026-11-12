# NetWasm.UI documentation

| Guide | What it covers |
|---|---|
| [Getting started](getting-started.md) | Prerequisites, the `dotnet new` template, the dev server |
| [Components and the C# API](components.md) | `Component`, elements, attributes, events, binding, `When`/`Each`, props and slots |
| [Templates (`.nwui`)](templates.md) | The Razor-like template syntax, the source generator and editor support |
| [Reactivity](reactivity.md) | `Signal`, `[Reactive]` properties, `Computed`, `Effect`, `Selector`, batching, scopes, context and lifecycle |
| [Services and DI](services.md) | `Services`, `Inject<T>()`, `@inject`, scoped and lazy services |
| [Async data and Suspense](async.md) | `Resource<T>`, `Suspense`, `HttpClient` and JSON |
| [Forms and validation](forms.md) | `Form`, `Field<T>`, validators, `@bind`, accessible errors |
| [Error handling](error-handling.md) | `ErrorBoundary`, `App.OnError`, where exceptions go |
| [Routing](routing.md) | `Routes`, `Route`, `Link`, `NavLink`, route parameters, nested routes, base path |
| [JavaScript interop](js-interop.md) | `[JSImport]`, `[JSExport]`, Promises, callbacks, wrapping JS libraries in components |
| [Embedding](embedding.md) | Several apps on one page, `createDomHost` options |
| [Publishing and size](deployment.md) | Release builds, what to deploy, hosting, caching, keeping the Wasm small |
| [Security](security.md) | Content Security Policy, Trusted Types, XSS |
| [Compatibility and versioning](compatibility.md) | Supported SDKs and browsers, the 0.x versioning policy |
| [Architecture](architecture.md) | How rendering works: the binary op batch, `dom.mjs`, events |
| [Benchmarks](benchmarks.md) | Comparison with SolidJS, React and Blazor WebAssembly |
| [Troubleshooting](troubleshooting.md) | Common problems and known limitations |

Working on the framework itself? See [CONTRIBUTING.md](../CONTRIBUTING.md).
