# Changelog

## Unreleased

- Highlight `@inject Type Name` like `@prop`.

## 0.1.0

- First Marketplace release.
- Syntax highlighting for `.nwui` templates on top of the HTML grammar: `@expr`, `@(expr)`,
  `@if`/`else`, `@foreach`, `@render(...)`, event and binding directives (`@onclick`, `@bind`,
  `@bind:event`, `@ref`, `@key`), component tags, `@prop`/`@using`/`@namespace`,
  `@code { }` blocks with embedded C#, `@* comments *@` and `@@` escapes.
- Comment toggling (`@* *@`), bracket matching and auto-closing.
