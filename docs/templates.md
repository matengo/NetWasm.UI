# Templates (`.nwui`)

Components can be written as `.nwui` files: HTML with Razor-style `@` expressions. A Roslyn
source generator turns each file into a `partial class` that calls the [C# API](components.md),
so there is no template engine at runtime and nothing reflection-based for NetWasm to trip over.
The static HTML of each element subtree becomes a [static template](components.md#static-templates-fast-rows)
that the browser parses once and clones; only bindings, handlers, `@expr` text and
components/blocks are set per instance.

```razor
@* CounterView.nwui → public partial class CounterView : Component *@
<section>
    <h2>Counter</h2>
    <p>Count: <strong>@count</strong> &middot; doubled: @doubled</p>
    <button @onclick="() => count.Value++">+1</button>
    <button disabled="@(count.Value == 0)" @onclick="() => count.Value = 0">Reset</button>
    @if (count.Value >= 10) {
        <p class="hint">That's a lot of clicks!</p>
    }
</section>

@code {
    private readonly Signal<int> count = new(0);
    private Computed<int> doubled = null!;

    partial void OnInit() => doubled = new Computed<int>(() => count.Value * 2);
}
```

## Setup

A `Foo.nwui` file becomes `public partial class Foo : Component` in `RootNamespace` plus the
folder path (or the namespace given with `@namespace X.Y`). For example, `Pages/Home.nwui` in
`MyApp` becomes `MyApp.Pages.Home`; add `using MyApp.Pages;` where you use it from another
namespace.

The `NetWasm.UI` package contains the generator and feeds it every `*.nwui` file in the
project, so there is nothing to set up. Set `<NetWasmUIEnableTemplates>false</NetWasmUIEnableTemplates>`
to turn it off. The dev server watches `.nwui` files like `.cs` files.

Every generated file has these usings, plus your own `@using` lines:

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using NetWasm.UI;
using NetWasm.UI.Forms;
using NetWasm.UI.Reactive;
using NetWasm.UI.Routing;
using static NetWasm.UI.Html;
```

## Syntax

| Syntax | Becomes |
|---|---|
| `@name`, `@a.b(c)[0]`, `@(x + 1)` | reactive text `Text(() => expr)`; a `Signal`/`Computed` prints (and tracks) its value |
| `@render(expr)` | any `Content` (node, component, fragment) inserted as-is |
| `class="a @b"`, `title="@x"` | static `Attr`, or reactive `Attr(name, () => …)` when it contains `@` |
| `disabled="@cond"` (also `checked`, `hidden`, `readonly`, `required`, `selected`, `multiple`, `autofocus`, `open`) | toggled attribute / `checked` property |
| `value="…"` | the `value` property (`Value(...)`) |
| `@onclick="() => …"`, `@onsubmit="Save"` | `On("click", handler)`; the handler is an `Action` or `Action<DomEvent>` |
| `@onclick:preventDefault`, `@onclick:stopPropagation`, `@onsubmit:preventDefault="false"` | listener flags (`submit` prevents default unless turned off) |
| `@bind="signal"` | `Spread(Bind(signal))` for `Signal<string>` / `Signal<bool>`, and for `Field<string>` / `Field<bool>` (with `id`, touched and ARIA state; see [Forms](forms.md)) |
| `@ref="field"` | assigns the `Element` (or component) to a generated field |
| `@if (c) { … } else if (c2) { … } else { … }` | `When(...)`; a chain shares one memoized selector |
| `@foreach (var x in items) { <li @key="x.Id">…</li> }` | keyed `Each(...)`; `items` may be a list expression or an `IReadOnlySignal<IReadOnlyList<T>>`; without `@key` the item itself is the key |
| `<Badge Label="hi" Count="@n" />` | `new Badge { Label = "hi", Count = n }` (an uppercase tag is a component) |
| `<Card>…</Card>` | `new Card { ChildContent = new Slot(() => …) }`; place it inside `Card` with `@render(ChildContent)` |
| `<Card><template slot="Header">…</template>…</Card>` | a named slot: `Header = new Slot(() => …)`; the rest still goes to `ChildContent` |
| `<Link href="/x">…</Link>`, `<NavLink href="/x" activeClass="on">…</NavLink>` | `Html.Link` / `Html.NavLink` |
| `<ErrorBoundary>…<template slot="Fallback" context="error">…</template></ErrorBoundary>` | `Html.ErrorBoundary(() => …, (BoundaryError error) => …)`; see [Error handling](error-handling.md) |
| `<Suspense>…<template slot="Fallback">…</template></Suspense>` | `Html.Suspense(() => …, () => …)`; the content must have exactly one root node, and the `<Suspense>` cannot be the only content of a template or block. See [Async data](async.md) |
| `@code { … }`, `@using X`, `@namespace X` | class members, usings, namespace |
| `@prop int Level = 1;` | a reactive prop: `public Prop<int> Level { get; set; } = 1;` |
| `@* … *@`, `<!-- … -->`, `@@`, `&amp; &times; &minus; &#65;` | comments, a literal `@`, decoded entities (common named ones plus numeric; an unknown `&name;` is an error) |

## Rules

- `Build()` calls `partial void OnInit()` first, inside the component's scope. Implement it in
  `@code` to call `Use`, `Provide` or `OnMount`, or to create `Computed`s.
- Whitespace follows JSX: text on one line is kept, multi-line text is trimmed per line and joined
  with single spaces, and whitespace-only lines disappear.
- Each template, `@if`/`else` block and `@foreach` body must have exactly one root node. `@if` and
  `@foreach` must sit inside an element.
- Literal attribute values are strings, so pass other types as code: `Step="@(2)"`, `Max="@max"`.

## Props

Plain component properties (`public int Count { get; set; }`) are evaluated once, when the
parent builds.

Reactive props follow the parent's signals. Declare them with `@prop Type Name [= default];` (or
any `Prop<T>` property, also in hand-written C# components). The parent's `Level="@level"` or
`Label="Hi @name.Value"` is then passed as a getter (`Level = new(() => level)`):

```razor
@* Stepper.nwui: Value is a Signal the parent owns (two-way); the rest are reactive props. *@
<div class="stepper">
    <span class="stepper-label">@Label</span>
    <button disabled="@(Value.Value <= Min)" @onclick="() => Add(-Step)">&minus;</button>
    <output>@Value</output>
    <button disabled="@(Value.Value >= Max)" @onclick="() => Add(Step)">+</button>
</div>

@prop string Label = "";
@prop int Step = 1;
@prop int Min = 0;
@prop int Max = 10;

@code {
    public Signal<int> Value { get; set; } = new(0);

    private void Add(int delta)
    {
        var next = Value.Value + delta;
        Value.Value = next < Min ? Min : next > Max ? Max : next;
    }
}
```

```razor
<Stepper Label="Guests (max @max)" Value="@guests" Max="@max" Step="@(2)" />
```

Read a prop with `@Label`, in `@if (Max > 3)`, in `disabled="@(…)"` or as `Max.Value` inside a
`Func`; those reads are tracked. Reading `Max.Value` in `OnInit` or other `@code` gives a
snapshot, because `Build` is untracked. From C#: `new Stepper { Max = maxSignal }`,
`Max = new(() => …)` or `Max = 5`.

## Child content

Declare `public Slot? ChildContent { get; set; }` (plus any named slots, such as `Header`) and
place it with `@render(ChildContent)`. An unset slot renders nothing.

```razor
@* Panel.nwui *@
<div class="panel">
    <div class="panel-head">
        <h3>@Title</h3>
        @if (Actions is not null) {
            <div class="panel-actions">@render(Actions)</div>
        }
    </div>
    @render(ChildContent)
</div>

@prop string Title = "";

@code {
    public Slot? Actions { get; set; }
    public Slot? ChildContent { get; set; }
}
```

```razor
<Panel Title="Room">
    <template slot="Actions"><button @onclick="Reset">Reset</button></template>
    <p>Temperature: @temp</p>
</Panel>
```

The content is built where the component renders it, so it belongs to the component's scope (and
is torn down with it) while still reading the parent's signals.

`samples/Counter/Components` shows nesting: `Panel.nwui` (slots) and `Stepper.nwui` (`@prop`,
two-way `Signal<int>`) are templates, `ProgressBar.cs` is a C# component, and `RoomRazor.nwui` /
`RoomCode.cs` build the same UI from them, once as a template and once in C#.

## Errors

Template errors are reported with the position in the `.nwui` file:

| Code | Meaning |
|---|---|
| `NWUI001` | syntax error |
| `NWUI002` | structural error (for example, more than one root node) |
| `NWUI003` | unsupported construct |

User C# is emitted under `#line`, so C# errors also point at the template, for example
`CounterView.nwui(3,69): error CS0103`.

## Editor support

`tools/vscode-nwui` is a small VS Code extension that highlights `.nwui` files. It uses VS Code's
own HTML and C# grammars, so markup, `@expressions`, `@if`/`@foreach`, directive attributes
(`@onclick="…"`), components, `@prop` and `@code { … }` get their normal colors. It also sets up
`@* *@` comments, bracket matching and auto-closing.

```powershell
cd tools/vscode-nwui
npm install
npm run package                     # → nwui.vsix
code --install-extension nwui.vsix
npm test                            # grammar tests
```

There is no IntelliSense or go-to-definition inside templates yet. C# errors show up in the build
and point at the `.nwui` line.

## Not supported yet

- Control flow other than `@if`/`@foreach` (no `@switch`).
- Slots with parameters (`Slot<T>`).
