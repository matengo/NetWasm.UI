# Getting started

## Prerequisites

- .NET SDK 10.0.303 or later; .NET 11 works too and is needed for C# 15. `global.json` rolls
  forward to the latest installed SDK.
- The NetWasm SDK is pinned in `global.json` (`"msbuild-sdks": { "NetWasm.Sdk": "0.12.2" }`). It
  downloads its host tooling on first restore.
- A browser listed in [Compatibility](compatibility.md#browsers).

## From the template

```shell
dotnet new install NwUi.Templates
dotnet new netwasm-ui -o MyApp
cd MyApp
dotnet nwui-dev
```

`dotnet new` restores the project and the local `nwui-dev` tool. Open http://localhost:8765/.
The template contains:

| File | What it is |
|---|---|
| `MyApp.csproj` | A NetWasm page app that references `NwUi.Browser`. |
| `global.json` | Pins the NetWasm SDK. |
| `.config/dotnet-tools.json` | The `nwui-dev` dev server. |
| `Program.cs` | The `[JSExport("start")]` entry point. |
| `App.cs` | The root component: navigation and routes, written in C#. |
| `Pages/*.nwui` | Two pages written as templates. |
| `wwwroot/` | `index.html`, `app.mjs` and `app.css`. Published to the site root. |

## The pieces, by hand

To add NetWasm.UI to an existing NetWasm project, you need the same pieces as the template.

**`MyApp.csproj`**

```xml
<Project Sdk="NetWasm.Sdk">
  <PropertyGroup>
    <TargetFramework>netwasm0.1</TargetFramework>
    <OutputType>JsModule</OutputType>
    <NetWasmJavaScriptHosts>page</NetWasmJavaScriptHosts>
    <RootNamespace>MyApp</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="NwUi.Browser" Version="0.5.0" />
  </ItemGroup>
  <ItemGroup>
    <Content Include="wwwroot\**" CopyToPublishDirectory="PreserveNewest" Link="%(RecursiveDir)%(Filename)%(Extension)" />
  </ItemGroup>
</Project>
```

The packages set up the rest:

- `NwUi.Browser` adds the `nwui.dom` JavaScript module to the app. Set
  `<NetWasmUIAddDomModule>false</NetWasmUIAddDomModule>` to supply your own module, for example
  a custom `createDomHost` (see [Embedding](embedding.md)).
- `NetWasm.UI` feeds every `*.nwui` file to the template compiler. Set
  `<NetWasmUIEnableTemplates>false</NetWasmUIEnableTemplates>` to turn that off.

**`Program.cs`** – the entry point that JavaScript calls:

```csharp
using System.Runtime.InteropServices.JavaScript;
using NetWasm.UI.Browser;

namespace MyApp;

public static partial class Program
{
    public static void Main()
    {
    }

    [JSExport("start")]
    public static void Start() => BrowserApp.Start(new App(), "#app");
}
```

`BrowserApp.Start` connects the DOM host, installs the browser router and mounts the root
component into the element that matches the selector.

**`App.cs`** – the root component:

```csharp
using NetWasm.UI;
using NetWasm.UI.Reactive;
using static NetWasm.UI.Html;

namespace MyApp;

public sealed class App : Component
{
    private readonly Signal<int> count = new(0);

    protected override Node Build() =>
        Main(
            H1("Hello NetWasm.UI"),
            Button(OnClick(() => count.Value++), "Clicked ", Text(count), " times"));
}
```

**`wwwroot/index.html`** and **`wwwroot/app.mjs`** – the page and the module that starts the app. Use absolute
URLs so that deep links such as `/todos/3` still find the assets:

```html
<!doctype html>
<html lang="en">
<head><meta charset="utf-8" /><title>MyApp</title></head>
<body>
  <div id="app"></div>
  <script type="module" src="/app.mjs"></script>
</body>
</html>
```

```js
import { open } from "/browser/MyApp.mjs";

const app = await open();
app.start();
```

## Developing

```shell
dotnet nwui-dev            # in the app folder; or: dotnet nwui-dev path/to/MyApp
```

The dev server does the following:

- publishes the app;
- serves it with the right MIME types, Brotli/gzip and a fallback to `index.html` for deep
  links;
- watches `.cs`, `.nwui` and asset files;
- reloads the browser after each rebuild;
- shows build errors as an overlay in the page.

Install it outside the template with `dotnet tool install NwUi.DevServer`.

`dotnet run` from the NetWasm SDK also serves the app, but it has no deep-link fallback, so a
reload on `/counter` returns 404.

| Option | Meaning |
|---|---|
| `--port <n>` | Port (default 8765) |
| `-O O0\|O1\|Oz` | NetWasm optimization (default `O0`: only changed methods are recompiled) |
| `--release` | Same as `-c Release -O Oz`: production sizes |
| `-c Debug\|Release` | Configuration (default `Debug`) |
| `--no-watch` | Serve only |
| `--no-publish` | Skip the initial publish |
| `-w <dir>` | Watch root (default: the nearest folder containing `.git` or `global.json`) |

There is no hot reload. NetWasm compiles ahead of time, so there is no runtime to patch. A
rebuild after a one-line change takes about 3–4 s from save to reloaded page; see
[CONTRIBUTING.md](../CONTRIBUTING.md#rebuild-time) for where the time goes.

The reload keeps your state:

- `Signal<int>`, `Signal<bool>` and `Signal<string>` values (also inside `Computed` inputs,
  components and services);
- text fields, checkboxes and selects, the focused element and the cursor position;
- the scroll position.

Signals are matched by the order they are created in and by their initial value. A signal whose
initial value changed in the edit, or that is created in a different order (for example after
you navigated elsewhere), starts fresh. Lists and other object values are not kept. Pressing F5
always starts clean.

State is kept only in `Debug` builds: the source generator then adds a module initializer that
connects to the dev server. `--release` and production builds do not include it. Set
`<NetWasmUIDevState>false</NetWasmUIDevState>` to turn it off, or `true` to force it on.

## Next

- [Components and the C# API](components.md) or [Templates (`.nwui`)](templates.md) to build UI.
- [Reactivity](reactivity.md) for state.
- [Publishing and size](deployment.md) when you are ready to ship.
