# NwUi.DevServer

Development server for [NetWasm.UI](https://github.com/matengo/NetWasm.UI) apps.

```shell
dotnet tool install --local NwUi.DevServer
dotnet nwui-dev                 # in the app folder; http://localhost:8765
dotnet nwui-dev --help
```

It publishes the app (with `NetWasmOptimization=O1` for speed), serves the publish folder with
Brotli/gzip and an SPA fallback to `index.html`, and on every saved change rebuilds and reloads
the browser. Use `--release` to see production (`Oz`) sizes. Not meant for production hosting.
