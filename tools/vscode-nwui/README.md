# NetWasm.UI templates (`.nwui`) for VS Code

Syntax highlighting for NetWasm.UI `.nwui` templates. It covers HTML with embedded C#: `@expr`,
`@(expr)`, `@if`/`else`/`@foreach`, `@render(...)`, directive attributes (`@onclick`, `@bind`,
`@ref`, `@key`, `:preventDefault`), components and slots, `@prop`, `@using`, `@namespace`,
`@code { … }` and `@* comments *@`.

```powershell
npm install
npm run package                     # → nwui.vsix
code --install-extension nwui.vsix
npm test
```
