# NetWasm.UI documentation

| Guide | What it covers |
|---|---|
| [Getting started](getting-started.md) | Prerequisites, the `dotnet new` template, the dev server |
| [Components and the C# API](components.md) | `Component`, elements, attributes, events, binding, `When`/`Each`, props and slots |
| [Templates (`.nwui`)](templates.md) | The Razor-like template syntax, the source generator and editor support |
| [Reactivity](reactivity.md) | `Signal`, `[Reactive]` properties, `Computed`, `Effect`, `Selector`, batching, scopes, context, events (`EventChannel`) and lifecycle |
| [Services and DI](services.md) | `Services`, `Inject<T>()`, `@inject`, scoped and lazy services |
| [Async data and Suspense](async.md) | `Resource<T>`, `Suspense`, `HttpClient` and JSON |
| [Forms and validation](forms.md) | `Form`, `Field<T>`, validators, `@bind`, accessible errors |
| [Build a component library](component-library-tutorial.md) | Tutorial: a headless library, its tests, a themed library on top, and packages |
| [Building a headless library](headless-libraries.md) | Layering, API conventions, compound components, dialog and menu patterns, an accessibility checklist |
| [Testing](testing.md) | Rendering components in unit tests with `NwUi.Testing` (preview) |
| [Error handling](error-handling.md) | `ErrorBoundary`, `App.OnError`, where exceptions go |
| [Routing](routing.md) | `Routes`, `Route`, `Link`, `NavLink`, route parameters, nested routes, base path |
| [Web Workers](workers.md) | `NwUi.Workers` (preview): C# services in Web Workers, pools, cancellation, progress and streams |
| [JavaScript interop](js-interop.md) | `[JSImport]`, `[JSExport]`, Promises, callbacks, wrapping JS libraries in components |
| [Libraries and packages](libraries.md) | NuGet and .NET Standard packages, what fails at run time, your own component libraries, size |
| [Embedding](embedding.md) | Several apps on one page, `createDomHost` options |
| [Publishing and size](deployment.md) | Release builds, what to deploy, hosting, caching, keeping the Wasm small |
| [Security](security.md) | Content Security Policy, Trusted Types, XSS |
| [Compatibility and versioning](compatibility.md) | Supported SDKs and browsers, the 0.x versioning policy |
| [Architecture](architecture.md) | How rendering works: the binary op batch, `dom.mjs`, events |
| [Benchmarks](benchmarks.md) | Comparison with SolidJS, React and Blazor WebAssembly |
| [Troubleshooting](troubleshooting.md) | Common problems and known limitations |

Working on the framework itself? See [CONTRIBUTING.md](../CONTRIBUTING.md).
