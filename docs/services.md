# Services and dependency injection

NetWasm.UI has a small, reflection-free form of dependency injection built on the same scope tree
as [context](reactivity.md#context-and-stores). You register services by type, and any component
below the registration resolves them with `Inject<T>()` or the `@inject` template directive.

There is no runtime container, no reflection and no assembly scanning. Constructors are wired by
the source generator at compile time. That keeps it trim-safe and small in Wasm, and it means
lifetimes follow the component tree.

## App-wide services

Register them when you start the app:

```csharp
using NetWasm.UI;
using NetWasm.UI.Browser;

var services = new Services()
    .Add<IClock>(new SystemClock())          // an existing instance
    .Add(new HttpClient())                   // registered as HttpClient
    .AddLazy<IWeatherApi, WeatherApi>();     // created on first Inject, constructor injected

BrowserApp.Start(new App(), services);
```

Then use them in any component while it builds:

```csharp
public sealed class Forecast : Component
{
    protected override Node Build()
    {
        var api = Inject<IWeatherApi>();
        var data = Resource.Create(ct => api.GetForecastAsync(ct));
        return P(Text(() => data.Value ?? "Loading…"));
    }
}
```

In a `.nwui` template, declare the service with `@inject Type Name`. The field is set before
`OnInit` runs, so you can use it there and in the markup:

```razor
<p>@forecast.Value</p>

@inject IWeatherApi Api

@code {
    private Resource<string> forecast = null!;

    partial void OnInit() => forecast = Resource.Create(ct => Api.GetForecastAsync(ct));
}
```

### Constructor injection

A component can also take its services as constructor parameters:

```csharp
public sealed class Forecast(IWeatherApi api) : Component
{
    protected override Node Build()
    {
        var data = Resource.Create(ct => api.GetForecastAsync(ct));
        return P(Text(() => data.Value ?? "Loading…"));
    }
}
```

In a `.nwui` template, `<Forecast />` compiles to `new Forecast(Inject<IWeatherApi>())`. The
parent resolves each parameter while it builds, from the parent's scope. This also works for a
template's code-behind (`public partial class Forecast(IWeatherApi api)` next to
`Forecast.nwui`). In C#, pass the service yourself: `new Forecast(Inject<IWeatherApi>())`,
also inside a `Route` lambda.

The template generator injects a component's constructor only when it is the component's single
public or internal constructor and it has parameters. With a parameterless constructor, or several
constructors, the tag uses `new Forecast { ... }` as before. `ref`, `out` and `params` parameters
are not injected.

## Auto-wired services

`AddLazy<TService, TImplementation>()` registers a class whose constructor parameters are
themselves services. `AddLazy<T>()` does the same with `T` as both key and implementation:

```csharp
public sealed class WeatherApi(HttpClient http, IClock clock) : IWeatherApi { ... }

var services = new Services()
    .Add(new HttpClient { BaseAddress = new Uri(origin) })
    .Add<IClock>(new SystemClock())
    .AddLazy<IWeatherApi, WeatherApi>()   // IWeatherApi -> new WeatherApi(Inject<HttpClient>(), Inject<IClock>())
    .AddLazy<ForecastCache>();            // ForecastCache -> new ForecastCache(...)
```

The NetWasm.UI source generator rewrites each call at compile time, using a
[C# interceptor](https://github.com/dotnet/roslyn/blob/main/docs/features/interceptors.md), into
the factory you would otherwise write by hand:

```csharp
services.AddLazy<IWeatherApi>(static () => new WeatherApi(Inject<HttpClient>(), Inject<IClock>()));
```

So it behaves exactly like `AddLazy<T>(factory)`: one instance per providing scope, created on the
first `Inject`, disposed with the scope, and the parameters resolve from the providing scope. A
missing dependency throws on that first `Inject`, like a missing service anywhere else.

Rules, checked at build time (error `NWUI010`):

- The implementation must be a non-abstract class that the calling project can see (public or
  internal, not a private nested type).
- It must have exactly one public or internal constructor. With several, register a factory.
- Constructor parameters cannot be `ref`, `out`, `in` or `params`. Every other parameter is
  resolved with `Inject<T>()` of its declared type, so register a primitive, `string` or option
  object with a factory instead.
- The type arguments must be concrete at the call site. Inside a generic method,
  `AddLazy<T>()` with `T` a type parameter is an error.

The NwUi package sets `InterceptorsNamespaces` for you. If you reference the generator some other
way, add `<InterceptorsNamespaces>$(InterceptorsNamespaces);NetWasm.UI.Generated</InterceptorsNamespaces>`
to the project. If the call is not rewritten, it throws `InvalidOperationException` at run time.

### Why not reflection or `DynamicallyAccessedMembers`?

`Microsoft.Extensions.DependencyInjection` finds constructors with reflection at run time.
`[DynamicallyAccessedMembers]` only tells the trimmer to keep those constructors so the
reflection still works after trimming. NetWasm compiles ahead of time without runtime reflection,
so there is nothing for that attribute to protect. Here the constructor is chosen when you build,
the generated code is an ordinary `new`, and a type that cannot be built is a compile error
instead of a run-time failure.

## Keys and lookup

- A service is registered under exactly the type argument. `Add(new WeatherApi())` registers
  `WeatherApi`, not `IWeatherApi`, so write `Add<IWeatherApi>(...)` when consumers inject the
  interface.
- Lookups walk up the scope tree, so the nearest registration wins. They work in routed pages
  and inside `When`/`Each` branches.
- `Inject<T>()` throws if nothing is registered. `TryInject<T>(out var value)` returns `false`
  instead, for optional services.

## Scoped services

`Provide(services)` registers services for the current component and everything it builds after
the call. `Provide(services, () => subtree)` registers them for one subtree only. Both override
registrations further up:

```csharp
protected override Node Build()
{
    Provide(new Services().Add<ILogger>(new PrefixLogger("[admin]")));
    return Div(new AdminMenu(), new AdminPage());
}
```

## Lifetimes

| You want | Register | Lives until |
|---|---|---|
| One instance for the whole app | `Add<T>(instance)`, `AddLazy<T>(factory)` or `AddLazy<TService, TImpl>()` in `BrowserApp.Start` | `BrowserApp.Stop()` / `App.Unmount()` |
| One instance per subtree (for example per page) | `AddLazy<T>(factory)` or `AddLazy<TService, TImpl>()` in `Provide(...)` | the providing component or branch is removed |
| A new instance on every resolve | `Add<Func<T>>(() => new T())`, then `Inject<Func<T>>()()` | — |

- `AddLazy` runs the factory on the first `Inject` in each providing scope. The same `Services`
  object can be provided in several places, and each place gets its own instance.
- A lazy instance that implements `IDisposable` is disposed when its providing scope is disposed,
  after the components that used it.
- Instances passed to `Add` belong to you and are never disposed.
- A factory runs in the providing scope, so it can `Inject` services registered at that level or
  above, but not ones a consumer provides further down. A factory that injects its own type
  throws "Circular service dependency".

## Rules and pitfalls

- Call `Inject` while building (in `Build`, `OnInit` or a branch builder), not inside a reactive
  lambda, an event handler or after an `await`. Keep the result in a local or field and use that
  later. Outside a build there is no current scope, so `Inject` throws.
- Services are plain objects. Put `Signal`s in them for state that the UI should follow.
- `Context<T>` is still the right tool when you want a default value or several values of the
  same type under different keys.
