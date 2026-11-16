# Libraries and packages

An app can use NuGet packages, its own class libraries and JavaScript libraries. This page says
what works with `netwasm0.1` (NetWasm.Sdk 0.14) and what it costs in download size.

## The framework is included

Every `netwasm0.1` project gets NetWasm's framework without a package reference: collections,
LINQ, `System.Text.Json`, regular expressions, XML, `HttpClient`, compression, cryptography and
more.

- Do not reference the old `NetWasm.System.*` packages (`NetWasm.System.Text.Json`,
  `NetWasm.System.Net.Http`, `NetWasm.System.Linq` and the rest). Remove them when you upgrade
  from NetWasm.Sdk 0.13 or earlier.
- Microsoft packages for assemblies that are already in the framework, such as
  `System.Text.Json` or `System.Memory`, are not needed either. The SDK prunes them.

Libraries that are not part of the framework come as separate NetWasm packages. Use the release
that matches your NetWasm.Sdk:

| Package | What |
|---|---|
| `NetWasm.Microsoft.Extensions.DependencyInjection` | Microsoft DI, with activation generated at build time |
| `NetWasm.Microsoft.Extensions.Logging`, `.Configuration.*`, `.Options`, `.Caching.*`, `.Http`, `.Primitives` | The rest of Microsoft.Extensions |
| `NetWasm.System.IO.Hashing`, `NetWasm.System.Formats.Cbor` | Hashing and CBOR |

## NuGet packages

Add a package as usual:

```powershell
dotnet add package Humanizer.Core
```

NuGet picks the package's `netwasm0.1` asset if it has one. Otherwise it picks its .NET Standard
2.1, 2.0 or earlier asset. A package that ships only `net8.0` or later assets fails to restore
with `NU1202`.

Code that NetWasm does not support usually still compiles, and it throws when it runs:

- **Reflection.** Finding members at run time (`GetProperties`, `GetConstructors`,
  `Type.GetType(string)` and so on) throws `PlatformNotSupportedException`. Newtonsoft.Json builds
  but throws when it serializes; use `System.Text.Json` with source generation.
- **Dependency injection.** Microsoft DI finds constructors by reflection for registrations such
  as `AddSingleton<IService, Service>()`. Register a factory instead, use
  `NetWasm.Microsoft.Extensions.DependencyInjection`, or use NetWasm.UI's own
  [Services](services.md).
- **Resources.** NetWasm deploys no embedded resources. Exception messages from Microsoft.Extensions
  and many other packages show the resource key, such as
  `TryAddIndistinguishableTypeToEnumerable`, instead of a sentence.
- **File watching.** `FileSystemWatcher` throws. For Microsoft.Extensions.Hosting, pass
  `--hostBuilder:reloadConfigOnChange=false`.
- **Native code.** A `[DllImport]` of a library that is not linked throws
  `DllNotFoundException`, and the build warns with `NW1017`.

So a successful build does not prove that a package works: run the code paths you use in a
browser. NetWasm's
[.NET Standard libraries](https://github.com/zion-sati/NetWasm/blob/main/docs/netstandard-libraries.md)
page has the details and advice for library authors.

## JSON and HTTP

Serialize with a source-generated `JsonSerializerContext`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

[JsonSerializable(typeof(User[]))]
public sealed partial class ApiJson : JsonSerializerContext;

var users = JsonSerializer.Deserialize(json, ApiJson.Default.UserArray);
```

See [Async data and Suspense](async.md#httpclient-and-json) and `samples/Fetch`.

## Your own libraries

Reference a class library with `ProjectReference`:

- **Plain C# code** can target `netstandard2.0`, `netstandard2.1` or `netwasm0.1`.
- **Components and `.nwui` templates** need a `netwasm0.1` library that references `NwUi`. The
  `.nwui` files are compiled in the library, and the app uses the components like its own:

```xml
<Project Sdk="NetWasm.Sdk">
  <PropertyGroup>
    <TargetFramework>netwasm0.1</TargetFramework>
    <OutputType>Library</OutputType>
    <RootNamespace>MyComponents</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="NwUi" Version="0.11.0" />
  </ItemGroup>
</Project>
```

A `.nwui` file in the app then needs `@using MyComponents` to use `<Greeting Name="..." />`.

For dialogs, popovers, menus and other interactive components, see
[Building component libraries](components.md#building-component-libraries) and
`samples/Primitives`.

## JavaScript libraries

Load the library from your page or from a JavaScript module, and call it with `[JSImport]`. See
[JavaScript interop](js-interop.md#wrapping-a-js-library-in-a-component) and `samples/Interop`.

## Size

NetWasm compiles the app and every library into one Wasm module and keeps only the code the app
reaches. A large library still adds a lot. In Release (`Oz`, NetWasm.Sdk 0.14.2):

| App | `.wasm` (Brotli) |
|---|---:|
| `samples/Counter`: router, forms, components | 88 KB |
| `samples/Fetch`: `HttpClient` and `System.Text.Json` | 435 KB |

To see what a package costs, publish in Release before and after you add it and compare; see
[Publishing and size](deployment.md#size). `wasm-opt` at `Oz` can be slow with large
libraries; see [Troubleshooting](troubleshooting.md#the-build-is-very-slow).
