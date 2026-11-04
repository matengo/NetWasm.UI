# Services and dependency injection

NetWasm.UI has a small, reflection-free form of dependency injection built on the same scope tree
as [context](reactivity.md#context-and-stores). You register services by type, and any component
below the registration resolves them with `Inject<T>()` or the `@inject` template directive.

There is no container, no constructor injection and no assembly scanning. That keeps it
trim-safe and small in Wasm, and it means lifetimes follow the component tree.

## App-wide services

Register them when you start the app:

```csharp
using NetWasm.UI;
using NetWasm.UI.Browser;

var services = new Services()
    .Add<IClock>(new SystemClock())                        // an existing instance
    .AddLazy<IWeatherApi>(() => new WeatherApi(new HttpClient())); // created on first Inject

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
| One instance for the whole app | `Add<T>(instance)` or `AddLazy<T>(factory)` in `BrowserApp.Start` | `BrowserApp.Stop()` / `App.Unmount()` |
| One instance per subtree (for example per page) | `AddLazy<T>(factory)` in `Provide(...)` | the providing component or branch is removed |
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
