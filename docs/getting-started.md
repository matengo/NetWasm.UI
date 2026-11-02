# Getting started

## Prerequisites

- .NET SDK 10.0.3xx or later (`global.json` rolls forward to the latest feature band).
- The NetWasm SDK is resolved from `global.json` (`"msbuild-sdks": { "NetWasm.Sdk": "0.10.0" }`) and
  downloads its host tooling on first restore.

Run the sample to check that everything works:

```powershell
dotnet run --project tools/DevServer -- samples/Counter
# → publishes, serves http://localhost:8765/ and rebuilds + reloads the browser on save
```

## A new app

NetWasm.UI is not on NuGet yet, so an app references the projects in this repository. The
smallest app is five files plus `global.json`; `samples/Counter` is the complete version.

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
    <ProjectReference Include="..\NetWasm.UI\src\NetWasm.UI\NetWasm.UI.csproj" />
    <ProjectReference Include="..\NetWasm.UI\src\NetWasm.UI.Browser\NetWasm.UI.Browser.csproj" />
    <!-- Optional: compiles *.nwui templates into components (see templates.md). -->
    <ProjectReference Include="..\NetWasm.UI\src\NetWasm.UI.Generator\NetWasm.UI.Generator.csproj"
                      OutputItemType="Analyzer" ReferenceOutputAssembly="false"
                      SetTargetFramework="TargetFramework=netstandard2.0" SkipGetTargetFrameworkProperties="true" />
  </ItemGroup>
  <ItemGroup>
    <AdditionalFiles Include="**\*.nwui" Exclude="bin\**;obj\**" />
    <CompilerVisibleProperty Include="RootNamespace;ProjectDir" />
  </ItemGroup>
  <ItemGroup>
    <!-- The DOM decoder is declared by the app and bundled into netwasm.page.mjs. -->
    <NetWasmJavaScriptModule Include="..\NetWasm.UI\src\NetWasm.UI.Browser\dom.mjs" Module="netwasm.ui.dom" />
    <Content Include="index.html;app.mjs" CopyToPublishDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

`global.json` must pin the NetWasm SDK; copy it from this repository.

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

**`index.html`** and **`app.mjs`** – the page and the module that starts the app. Use absolute
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

```powershell
dotnet run --project path/to/NetWasm.UI/tools/DevServer -- path/to/MyApp
```

The dev server publishes the app, serves it with the right MIME types and Brotli/gzip, watches
`.cs` and `.nwui` files and reloads the browser after each rebuild. Build errors are shown as an
overlay in the page.

| Option | Meaning |
|---|---|
| `--port <n>` | Port (default 8765) |
| `-O O0\|O1\|Oz` | NetWasm optimization (default `O1`, for faster rebuilds) |
| `--release` | Same as `-O Oz`: production sizes |
| `-c Debug\|Release` | Configuration (default `Release`; Debug is slower and larger) |
| `--no-watch` | Serve only |
| `--no-publish` | Skip the initial publish |
| `-w <dir>` | Watch root (default: the repository root) |

There is no hot reload. NetWasm compiles ahead of time, so there is no runtime to patch. A
rebuild after a one-line change takes about 15–20 s; see
[CONTRIBUTING.md](../CONTRIBUTING.md#why-a-rebuild-takes-15-20-s) for where the time goes.

## Next

- [Components and the C# API](components.md) or [Templates (`.nwui`)](templates.md) to build UI.
- [Reactivity](reactivity.md) for state.
- [Publishing and size](deployment.md) when you are ready to ship.
