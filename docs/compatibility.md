# Compatibility and versioning

## Supported versions

| NetWasm.UI | NetWasm.Sdk | Target framework | .NET SDK |
|---|---|---|---|
| 0.5.x | 0.11.1 | `netwasm0.1` | 10.0.3xx |
| 0.1.x–0.4.x | 0.10.7 | `netwasm0.1` | 10.0.3xx |

The NetWasm SDK version comes from `global.json` (`msbuild-sdks`). The template pins the version
in this table. A newer NetWasm.Sdk patch release usually works. A new minor version
may change the import/export conventions; wait for a NetWasm.UI release that supports it.

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
`NwUi.Browser` and `NwUi.DevServer` are released with the same version number.
