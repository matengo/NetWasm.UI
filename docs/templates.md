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

The `NwUi` package contains the generator and feeds it every `*.nwui` file in the
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
| `@render(Row, item)` | a `Slot<T>` rendered with an argument (`Row.With(item)`); an unset slot renders nothing. See [Generic components](#generic-components-and-slots-with-parameters) |
| `class="a @b"`, `title="@x"` | static `Attr`, or reactive `Attr(name, () => …)` when it contains `@`; a `bool` value is written `"true"`/`"false"` (for `aria-*` and `data-*`) |
| `disabled="@cond"` (also `checked`, `hidden`, `readonly`, `required`, `selected`, `multiple`, `autofocus`, `open`, `inert`, `novalidate`, `formnovalidate`, `default`, `playsinline`, `nomodule`) | toggled attribute / `checked` property |
| `value="…"` | the `value` property (`Value(...)`) |
| `@onclick="() => …"`, `@onsubmit="Save"` | `On("click", handler)`; the handler is an `Action` or `Action<DomEvent>` |
| `@onclick:preventDefault`, `@onclick:stopPropagation`, `@onsubmit:preventDefault="false"` | listener flags (`submit` prevents default unless turned off) |
| `@bind="signal"` | `Spread(Bind(signal))` for `Signal<string>` / `Signal<bool>`, and for `Field<string>` / `Field<bool>` (with `id`, touched and ARIA state; see [Forms](forms.md)) |
| `@ref="field"` | assigns the `Element` (or component) to a generated field |
| `@if (c) { … } else if (c2) { … } else { … }` | `When(...)`; a chain shares one memoized selector |
| `@foreach (var x in items) { <li @key="x.Id">…</li> }` | a keyed list (see [Rows of an `@foreach`](#rows-of-an-foreach)); `items` may be a list expression or an `IReadOnlySignal<IReadOnlyList<T>>`; without `@key` the item itself is the key |
| `<Badge Label="hi" Count="@n" />` | `new Badge { Label = "hi", Count = n }` (an uppercase tag is a component; constructor parameters are filled with `Inject<T>()`, see [constructor injection](services.md#constructor-injection)) |
| `<Button Variant="ghost" class="w-4" aria-label="Save" @onclick="Save" />` | lowercase or dashed names and `@on…` handlers on a component tag go to `Attributes`; see [Attribute splatting](#attribute-splatting) |
| `@attributes="expr"` | applies a `Content[]` (such as a component's `Attributes`) to the element or passes it on to the component |
| `<Card>…</Card>` | `new Card { ChildContent = new Slot(() => …) }`; place it inside `Card` with `@render(ChildContent)` |
| `<Card><template slot="Header">…</template>…</Card>` | a named slot: `Header = new Slot(() => …)`; the rest still goes to `ChildContent` |
| `<template slot="Row" context="item">…</template>` | a slot with a parameter: `Row = new(item => …)` for a `Slot<T>` property |
| `<Select TItem="Fruit" …>` | `new Select<Fruit> { … }`: a generic component needs each type argument as an attribute |
| `@typeparam TItem where TItem : notnull` | makes the template's class generic (`Select<TItem>`); repeat for more type parameters |
| `<Dialog @bind-Open="isOpen" />` | `Open = isOpen`: shares the parent's `Signal<T>` with a `Signal<T>` property, so both sides read and write it |
| `<Link href="/x">…</Link>`, `<NavLink href="/x" activeClass="on">…</NavLink>` | `Html.Link` / `Html.NavLink` |
| `<ErrorBoundary>…<template slot="Fallback" context="error">…</template></ErrorBoundary>` | `Html.ErrorBoundary(() => …, (BoundaryError error) => …)`; see [Error handling](error-handling.md) |
| `<Suspense>…<template slot="Fallback">…</template></Suspense>` | `Html.Suspense(() => …, () => …)`; the content must have exactly one root node, and the `<Suspense>` cannot be the only content of a template or block. See [Async data](async.md) |
| `@code { … }`, `@using X`, `@namespace X` | class members, usings, namespace |
| `@prop int Level = 1;` | a reactive prop: `public Prop<int> Level { get; set; } = 1;` |
| `@inject IWeatherApi Weather` | a field set from `Inject<IWeatherApi>()` before `OnInit`; see [Services and DI](services.md) |
| `@* … *@`, `<!-- … -->`, `@@`, `&amp; &times; &minus; &#65;` | comments, a literal `@`, decoded entities (common named ones plus numeric; an unknown `&name;` is an error) |

## Rules

- `Build()` calls `partial void OnInit()` first, inside the component's scope. Implement it in
  `@code` to call `Use`, `Provide` or `OnMount`, or to create `Computed`s.
- State can be a `Signal<T>` field or a `[Reactive] private partial int Count { get; set; }`
  property, which you read as `@Count` and write as `Count++`. See
  [Reactive properties](reactivity.md#reactive-properties).
- Whitespace follows JSX: text on one line is kept, multi-line text is trimmed per line and joined
  with single spaces, and whitespace-only lines disappear.
- Each template, `@if`/`else` block and `@foreach` body must have exactly one root node. `@if` and
  `@foreach` must sit inside an element.
- Literal attribute values are strings, so pass other types as code: `Step="@(2)"`, `Max="@max"`.

## Rows of an `@foreach`

A list keeps one row per key. Adding, removing and reordering items moves the existing rows; only
new keys get new rows.

- **A row follows its key's item.** When the list is replaced by new objects with the same keys
  (immutable records, a refetch), every row shows its key's new object, in all of its content:
  - A row that is only `@expr` text, bound attributes and `@on…` handlers for
    [delegated events](architecture.md#event-delegation) keeps its DOM and takes the new object.
    Only the values that differ are written.
  - A row with anything else (a component, an `@if` or `@foreach` block, `@bind`, `@ref`,
    `@attributes`, a handler for another event) is built again for the new object, in the same
    place. What lived in the old row starts over: a component's state, a typed-in value, focus.

  Keep the same object to keep the row. An item counts as different when it is another object,
  or for a value type when it is not equal to the old value.
- **A row's bindings run together.** The `@expr` text and bound attributes of a row's own
  elements are one effect per row. When something one of them reads changes, all of them are
  evaluated again and only the values that differ are written to the DOM. Keep expensive work in
  a `Computed`, not in a row's expression.
- **A row's handlers cost nothing per row.** `@onclick` and the other
  [delegated events](architecture.md#event-delegation) on a row's elements are registered once for
  the whole list. A handler can use the loop variable as before:
  `@onclick="() => Remove(item.Id)"`.

This is what makes long lists cheap: a row is one template clone and one object, however many
bindings and handlers it has.

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

## Attribute splatting

A component tag takes more than its properties. HTML attributes and `@on…` event handlers written
on it are collected into the component's `Attributes`, and the component decides which element
they land on with `@attributes`. This is how a `Button` component can accept `class`,
`aria-label`, `disabled` or `@onclick` without declaring a property for each.

Try it in the [live example](https://matengo.github.io/NetWasm.UI/examples/splatting); its
source is in `site/Demos` (`SplatDemo.nwui`, `AppButton.nwui`, `IconButton.nwui`,
`TextField.nwui`).

### Which names go where

On a component tag:

| Written on the tag | Goes to |
|---|---|
| PascalCase name: `Variant="ghost"`, `Max="@(20)"` | the property of that name, as before |
| name that starts lowercase or contains `-`: `class`, `id`, `title`, `type`, `disabled`, `aria-*`, `data-*` | `Attributes` |
| `@onclick`, `@onkeydown:preventDefault`, any `@on…` with its modifiers | `Attributes` |
| `@attributes="expr"` | `Attributes` (the set is added in place, see [Forwarding](#forwarding)) |

Everything in `Attributes` keeps source order. `Attributes` is a `Content[]` and is empty when the
tag has none. `@ref` works on a component tag (and `@key` when it is the root of an `@foreach`
body), but `@bind` does not; for two-way binding, give the component a `Signal<T>` property (as
`Stepper` does above).

### Applying them

The component puts the set on its root element, or any element, with `@attributes`:

```html
@* AppButton.nwui *@
<button class="btn btn-@Variant" type="button" @attributes="Attributes">@render(ChildContent)</button>

@code {
    public string Variant { get; set; } = "default";
    public Slot? ChildContent { get; set; }
}
```

```html
<AppButton Variant="primary" class="wide" aria-label="Save" type="submit"
           disabled="@(name.Value.Length == 0)" @onclick="Save">Save</AppButton>
@* → <button class="btn btn-primary wide" type="submit" aria-label="Save" disabled>Save</button> *@
```

- **Order decides.** Attributes are applied in source order and a later one wins: `type="submit"`
  on the tag replaces the template's `type="button"`. A static attribute written *after*
  `@attributes` on the element wins over the splat.
- **`class` and `style` are joined**, not replaced: the element's own value, then each one from
  the set.
- **Bound values stay reactive.** `class="@(on ? "active" : null)"`, `disabled="@(…)"` and
  `aria-pressed="@(…)"` update the element when their signals change; `null` removes the
  attribute.
- A component that never writes `@attributes` ignores the set.

### Events on components

`@on…` handlers on a component tag become listeners on the element that applies `Attributes`. The
handler is an `Action` or an `Action<DomEvent>`, exactly as on an element, and the modifiers work
the same:

```html
<TextField Value="@draft" placeholder="Add a tag" @onkeydown="OnKey" />
<AppButton @onclick="Clear">Clear all</AppButton>

@code {
    private void OnKey(DomEvent e)
    {
        if (e.Key == "Escape") draft.Value = "";
    }
}
```

Handlers add up: if the component's template also has `@onclick` on that element, both run. The
event is a DOM event; for a value the component computes (a selected item, a validated date),
declare an `Action<T>` property instead (`OnSelect="@(item => …)"`).

### Forwarding

`@attributes` also works on a component tag, so a wrapper can add its own attributes and pass the
caller's on:

```html
@* IconButton.nwui *@
<AppButton Variant="ghost" class="btn-icon" @attributes="Attributes">@render(ChildContent)</AppButton>
```

```html
<IconButton aria-label="Remove" data-tag="@tag" @onclick="() => Remove(tag)">&times;</IconButton>
@* → <button class="btn btn-ghost btn-icon" type="button" aria-label="Remove" data-tag="…">×</button> *@
```

### Pitfalls

- Name properties in PascalCase. A property whose name starts lowercase is captured as an
  attribute and never assigned (a breaking change in this release, see the CHANGELOG).
- A component property named `Attributes` hides the base one.
- From C#, set `Attributes = new Content[] { Class("x"), On("click", Save) }` and apply them with
  `Splat(Attributes, baseClass: "btn")` (see [Components](components.md#attributes)).

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

## Generic components and slots with parameters

`@typeparam` makes a template generic, and a `Slot<T>` property is a slot that receives a value.
Together they let a list, table or select leave the item markup to the parent:

```razor
@* Select.nwui *@
@typeparam TItem where TItem : notnull
<ul role="listbox" @attributes="Attributes">
    @foreach (var item in Items) {
        <li role="option" aria-selected="@(Equals(item, Selected.Value))"
            @onclick="() => Selected.Value = item">@render(Option, item)</li>
    }
</ul>

@code {
    public Signal<IReadOnlyList<TItem>> Items { get; set; } = new(new TItem[0]);
    public Slot<TItem>? Option { get; set; }
    public Signal<TItem?> Selected { get; set; } = new(default);
}
```

```razor
<Select TItem="Fruit" Items="@fruits" @bind-Selected="picked">
    <template slot="Option" context="fruit"><span>@fruit.Emoji @fruit.Name</span></template>
</Select>
```

- **Type arguments are explicit.** Write `TItem="Fruit"` (the literal text is the C# type) for
  each type parameter; there is no inference from the other attributes yet. Generic components
  written in C# (`class Table<T> : Component`) work the same way.
- **`context="name"`** names the slot's parameter inside the `<template>`. Without `context`, the
  property must be a plain `Slot`.
- **`@render(Option, item)`** renders the slot with an argument. In C#, use `Option?.With(item)`.
- **`@bind-Selected="picked"`** passes the parent's `Signal<T>` to the `Signal<T>` property
  `Selected`. Both sides see each other's writes; it is the same signal.
- Test for an optional slot with `@if (Header is not null) { … }`.
- An `@if` branch is built once and only rebuilt when its condition flips, so content that
  depends on a value (such as `@render(Option, Selected.Value)`) belongs in an `@foreach` over a
  `Computed` list, or in reactive text and attributes.

`samples/Primitives` builds a dialog, popover, tabs, select and data table this way; see
[Building component libraries](components.md#building-component-libraries).

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

To install it, download `nwui.vsix` from the
[latest release](https://github.com/matengo/NetWasm.UI/releases/latest). Then run
`code --install-extension nwui.vsix`, or use **Extensions → … → Install from VSIX…** in VS Code.

To build it from source:

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
- Type inference for generic components: pass each type argument (`TItem="Fruit"`).
