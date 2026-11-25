# Compatibility and versioning

## Supported versions

| NetWasm.UI | NetWasm.Sdk | Target framework | .NET SDK |
|---|---|---|---|
| 0.16.x | 0.15.2 | `net10.0-netwasm` | 10.0.3xx or 11.0.x |
| 0.15.x | 0.14.2 | `netwasm0.1` | 10.0.3xx or 11.0.x |
| 0.14.x | 0.14.2 | `netwasm0.1` | 10.0.3xx or 11.0.x |
| 0.13.x | 0.14.2 | `netwasm0.1` | 10.0.3xx or 11.0.x |
| 0.12.x | 0.14.2 | `netwasm0.1` | 10.0.3xx or 11.0.x |
| 0.11.x | 0.14.2 | `netwasm0.1` | 10.0.3xx or 11.0.x |
| 0.10.x | 0.14.2 | `netwasm0.1` | 10.0.3xx or 11.0.x |
| 0.9.x | 0.13.0 | `netwasm0.1` | 10.0.3xx or 11.0.x |
| 0.6.x–0.8.x | 0.12.2 | `netwasm0.1` | 10.0.3xx or 11.0.x |
| 0.5.x | 0.11.1 | `netwasm0.1` | 10.0.3xx |
| 0.1.x–0.4.x | 0.10.7 | `netwasm0.1` | 10.0.3xx |

The NetWasm SDK version comes from `global.json` (`msbuild-sdks`). The template pins the version
in this table. A newer NetWasm.Sdk patch release usually works. A new minor version
may change the import/export conventions; wait for a NetWasm.UI release that supports it.

## Upgrading from `netwasm0.1`

NetWasm.Sdk 0.15 replaces the `netwasm0.1` target framework with `net10.0-netwasm`, a .NET platform
target framework like `net10.0-android`. NetWasm.UI releases from the first one in the table above
with `net10.0-netwasm` are built for it, and an app moves to them in one step:

1. **Pin the SDK.** In `global.json`, set `"msbuild-sdks": { "NetWasm.Sdk": "0.15.2" }`.
2. **Change the target framework.** In every app, worker and library project that targets
   `netwasm0.1`, change `<TargetFramework>netwasm0.1</TargetFramework>` to
   `<TargetFramework>net10.0-netwasm</TargetFramework>`, and replace `netwasm0.1` with
   `net10.0-netwasm` in any `<TargetFrameworks>` list. Projects that target `netstandard2.1`,
   `netstandard2.0` or `net10.0` do not change.
3. **Update the NwUi packages** to the version in the table in the same change. A `net10.0-netwasm`
   project cannot use a package built only for `netwasm0.1`, so older NwUi versions fail to
   restore their components and the `.nwui` source generator.
4. **Remove `NU1702` suppressions.** An app that references a .NET Standard project used to warn
   with `NU1702` and needed `<NoWarn>$(NoWarn);NU1702</NoWarn>`. A `net10.0-netwasm` app uses a
   .NET Standard library as any .NET 10 project does, without the warning, and
   `dotnet add reference` now accepts it.
5. **Skip the type-names file.** NetWasm.Sdk 0.15 writes `<App>.wasm.type-symbols` in Release too,
   and the page fetches it at startup. Unless the app reads type names at run time, add
   `<NetWasmTypeNames>false</NetWasmTypeNames>` to the app and to each worker project, as the
   templates do; otherwise deploy the file beside the app.
6. **Update paths.** Build and publish output moves from `bin/Release/netwasm0.1/` to
   `bin/Release/net10.0-netwasm/`. Update deployment scripts and CI steps that name it.

`net10.0-netwasm` also takes a package's `net10.0`, `net9.0` or `net8.0` build, newest first, where
`netwasm0.1` took only .NET Standard ones, so more NuGet packages restore. See
[Libraries](libraries.md).

## Browsers

NetWasm emits WebAssembly that uses exception handling with `exnref` and SIMD:

| Browser | Minimum version |
|---|---|
| Chrome, Edge and other Chromium browsers | 137 |
| Firefox | 131 |
| Safari (macOS and iOS) | 18.4 |

The E2E tests run in Playwright's Chromium, Firefox and WebKit on every pull request. Older
browsers fail when they compile the module. Show a message from `app.mjs` if `open()` throws.

## Versioning

NetWasm.UI follows [Semantic Versioning](https://semver.org) with the usual 0.x rule:

- **0.x.y patch releases** (0.1.0 → 0.1.1) fix bugs and do not change the public API.
- **0.x minor releases** (0.1 → 0.2) may change or remove public API. Every change is listed in
  the [CHANGELOG](../CHANGELOG.md) with migration notes.
- **1.0** will come when the API has been stable for a while. From then on, breaking changes
  only happen in major versions.

The public API is tracked by `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt` in each
library. A pull request that changes the API also changes these files, so the change is visible
in review. See [CONTRIBUTING.md](../CONTRIBUTING.md#public-api).

These are not part of the public API and may change in any release:

- the binary batch format between the renderer and `dom.mjs`;
- the `nwui.dom` import functions;
- the code the `.nwui` generator emits.

Do not depend on them. Use `createDomHost` and the documented options.

Upgrade all NetWasm.UI packages and tools together. The template, `NwUi`,
`NwUi.Browser`, `NwUi.Primitives` and `NwUi.DevServer` are released with the same version number.
