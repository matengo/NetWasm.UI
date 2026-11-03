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
