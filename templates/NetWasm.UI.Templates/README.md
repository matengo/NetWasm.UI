# NwUi.Templates

`dotnet new` templates for [NetWasm.UI](https://github.com/matengo/NetWasm.UI).

```shell
dotnet new install NwUi.Templates
dotnet new netwasm-ui -o MyApp
cd MyApp
dotnet tool restore
dotnet nwui-dev        # http://localhost:8765, rebuild + reload on save
```

`dotnet run` also works (NetWasm.Sdk's built-in server on http://localhost:5080), but it has no
SPA fallback: reloading a deep link such as `/counter` returns 404 there.

## Hosted in ASP.NET Core

```shell
dotnet new netwasm-ui-hosted -o MyApp
cd MyApp
dotnet watch --project MyApp.Server --no-hot-reload   # or: dotnet run --project MyApp.Server
```

This template needs the .NET 11 SDK. It creates three projects:

- `MyApp.Client`: the NetWasm.UI app, with Home, Counter and Weather pages.
- `MyApp.Server`: ASP.NET Core with the `/api/weatherforecast` API. It serves the client on the
  same origin, prerenders each route and live-reloads in development.
- `MyApp.Shared`: types shared by both projects.
