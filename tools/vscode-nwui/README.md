# NetWasm.UI templates (`.nwui`) for VS Code

Syntax highlighting for [NetWasm.UI](https://github.com/matengo/NetWasm.UI) `.nwui` templates. It
covers HTML with embedded C#: `@expr`, `@(expr)`, `@if`/`else`/`@foreach`, `@render(...)`, directive
attributes (`@onclick`, `@bind`, `@ref`, `@key`, `:preventDefault`), components and slots, `@prop`,
`@using`, `@namespace`, `@code { … }` and `@* comments *@`.

```razor
<button @onclick="() => count.Value++">Clicked @count times</button>

@code {
    private readonly Signal<int> count = new(0);
}
```

The extension only colours the file. The C# inside is compiled, and its errors reported, by the
NetWasm.UI source generator when you build. See the
[template guide](https://github.com/matengo/NetWasm.UI/blob/main/docs/templates.md).

## Install

Install **NetWasm.UI templates (.nwui)** from the Marketplace, or build a local package:

```powershell
npm install
npm run package                     # → nwui.vsix
code --install-extension nwui.vsix
npm test                            # grammar tests
```

Publishing (maintainers): `npx vsce publish -p <token>` with a Marketplace token for the
`netwasm-ui` publisher; the release workflow does this from the `VSCE_PAT` secret.
