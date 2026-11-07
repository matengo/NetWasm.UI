# NwUi.DevServer

Development server for [NetWasm.UI](https://github.com/matengo/NetWasm.UI) apps.

```shell
dotnet tool install --local NwUi.DevServer
dotnet nwui-dev                 # in the app folder; http://localhost:8765
dotnet nwui-dev --help
```

It publishes the app (`Debug` with `NetWasmOptimization=O0`, so only changed methods are
recompiled), serves the publish folder with Brotli/gzip and an SPA fallback to `index.html`, and
on every saved change rebuilds and reloads the browser. The reload keeps simple signal values
(`int`, `bool`, `string`), form fields, focus and scroll (Debug builds only; F5 starts clean). Use
`--release` to see production (`Release`, `Oz`) sizes. Not meant for production hosting.
