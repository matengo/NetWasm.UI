# Building a headless library

A headless library ships behaviour without looks: a dialog, a menu or tabs with the right
keyboard handling, focus moves and ARIA attributes, styled by the app or by a design-system
library on top. This page shows which NetWasm.UI pieces such a library is built from, the
conventions that make the components of different libraries look alike to callers, and the
patterns for the common widgets. `samples/Primitives.Kit` is a small library built this way,
`samples/Primitives.Theme` styles it, and `samples/Primitives` uses both. The kit targets
`netstandard2.1`, so the same build runs in the browser and in `net10.0` tests; see
[Your own libraries](libraries.md#your-own-libraries) for the project file.

New to this? [Build a component library](component-library-tutorial.md) goes from
`dotnet new netwasm-ui-lib` to a published package in six steps, and this page is the reference
behind it.

## Layers

A typical stack has three packages:

1. **Headless** (`MyUi.Headless`): components with behaviour, state and ARIA, and no CSS. They
   take `class` and any other attribute from their tag with
   [attribute splatting](templates.md#attribute-splatting), and publish their state as
   `data-state`, `aria-expanded` and similar attributes for CSS to select on.
2. **Design system** (`MyUi.Material`, `MyUi.Fluent`): thin components that wrap the headless
   ones, set classes, and ship a stylesheet as an [`NwUiAsset`](libraries.md#stylesheets-scripts-and-fonts).
   Each design system is a separate package with its own CSS.
3. **App**: references one design system (or the headless package and its own CSS), and
   overrides styles with its own stylesheet, which is linked after the libraries'.

## Use the platform first

The browser already does much of what headless libraries in other frameworks reimplement.
Prefer it, because it is smaller, accessible by default, and works before the Wasm has started:

| Need | Platform feature | NetWasm.UI |
|---|---|---|
| Modal dialog, focus trap, Escape, focus restore | `<dialog>` + `showModal()` | `element.ShowModal()`, `Close()` |
| Menus, popovers, light dismiss, top layer | the `popover` attribute | `ShowPopover()`, `HidePopover()`, the `toggle` event's `e.NewState` |
| Disclosure, accordion | `<details>`/`<summary>` | the `toggle` event |
| Making the page behind an overlay inactive | `inert` | the `inert` attribute |
| Enter and exit animations of a dialog or popover | `@starting-style`, `transition-behavior: allow-discrete` | CSS only |

Build in C# what the platform does not cover: roving tabindex, typeahead, a focus trap for a
non-modal panel, comboboxes and data tables.

## The pieces

| Need | API | More |
|---|---|---|
| Unique ids for `aria-labelledby`, `aria-controls`, `for` | `UseId(prefix)` | [Ids](components.md#building-component-libraries) |
| Shared state between the parts of a component (`Tabs` and its `Tab`s) | `Provide`/`Use` context | [Context and stores](reactivity.md#context-and-stores) |
| Outside click, Escape, focus leaving | `OnDocument(event, handler, within: panel)` and `e.Inside` | [Document and window listeners](components.md#building-component-libraries) |
| Scroll and touch | `ListenerFlags.Capture` / `Passive`, `@onscroll:passive` | [Events](components.md#events) |
| Calling DOM methods | `element.Call(...)`, `ShowModal()`, `Focus()`, `ScrollIntoView()` | [Element calls](components.md#building-component-libraries) |
| Code that runs after the DOM is up to date | `OnMount`, or an event handler | [Lifecycle](reactivity.md#lifecycle) |
| Moving focus into a panel | `element.FocusFirst()`, `FocusLast()` | [Focus](components.md#building-component-libraries) |
| Giving focus back | `ActiveElementId()`, then `Html.Focus(id)` on close | |
| Reading layout | `GetRect()`, `Viewport()`, `OnResize(element, …)` | [DOM reads](components.md#building-component-libraries) |
| Escaping `overflow: hidden` | `Portal(target, …)` | [Portal](components.md#building-component-libraries) |
| Content that animates out | `When(…).AnimateExit()`, `@if (…) animate-exit` | [Exit animations](components.md#exit-animations) |
| Events from a component to its parent | `@on…` handlers on the component tag, or `EventChannel<T>` for app-wide events | [Events on components](templates.md#events-on-components), [Events](reactivity.md#events) |
| Generic lists (`Select<TItem>`) | `@typeparam`, `Slot<T>` | [Generic components](templates.md#generic-components-and-slots-with-parameters) |
| CSS, fonts, JavaScript | `NwUiAsset` | [Libraries](libraries.md#stylesheets-scripts-and-fonts) |
| Positioning (anchors, flipping) | CSS anchor positioning, or a JS library such as Floating UI through `NwUiAsset` + `[JSImport]` + `BrowserDom.GetHandle` | [JavaScript interop](js-interop.md) |

## API conventions

Callers learn a library from one component and expect the rest to work the same. These are the
conventions `Primitives.Kit` follows; a library that keeps them lets an app swap one design system
for another without rewriting its markup.

**State: controlled, uncontrolled and a change callback.** Every piece of state a caller might
want to own gets three props:

| Prop | Type | Meaning |
|---|---|---|
| `Open`, `Value`, `Checked` | `Signal<T>?` | Controlled: the caller owns the state, and `@bind-Open` binds a field to it. |
| `DefaultOpen`, `DefaultValue` | `T` | Uncontrolled: the component keeps its own signal, starting here. |
| `OnOpenChange`, `OnValueChange` | `Action<T>?` | Called when the *user* changes the state, in both modes. |

```csharp
var open = Open ?? new Signal<bool>(DefaultOpen);   // one line covers both modes
```

Call the change callback only for user actions, not when the caller writes the signal, so a
caller that reacts by writing the signal does not loop.

**Attributes go on the root.** Splat `Attributes` on the element a caller would think of as the
component (the `Disclosure`'s wrapper, the `Tab`'s button), after the component's own attributes,
so `class`, `id`, `data-*`, `aria-*` and `@on…` handlers from the tag land there. `class` and
`style` are joined, any other attribute from the caller wins. If a part has a second element worth
styling (the trigger, the panel), make it a separate component, as `Tab` and `TabPanel` are, or
publish it through `data-state` so CSS can reach it from the root.

**Other props.**

- `Disabled` is a `Prop<bool>`, so it can be a constant or bound to a signal, and it sets both
  `disabled` (where the element supports it) and `data-disabled`.
- Content goes in `Slot`s: `ChildContent` for the main content, named slots (`Trigger`, `Title`)
  for the rest. Use `Slot<T>` when the content depends on an item (`Select<TItem>`).
- Props that callers pass rarely, such as `Vertical`, are plain values; read them once in `Build`.
- Name events `On<Something>` and give them the value, not the DOM event:
  `Action<string> OnValueChange`, not `Action<DomEvent>`.
- Keep the state type, the context and helpers `internal`; callers talk to the components only.

**State for CSS** is published as attributes, described in [State for styling](#state-for-styling).

## Compound components

A widget with several parts that the caller arranges (tabs, an accordion, a menu with its items)
is a set of components that share state through a context. `samples/Primitives.Kit/Tabs.cs` has
four: `Tabs` (the root), `TabList`, `Tab` and `TabPanel`. The caller writes the markup between them:

```html
<Tabs DefaultValue="b" class="tabs">
    <TabList aria-label="Fruit">
        <Tab Value="a">Apples</Tab>
        <Tab Value="b">Bananas</Tab>
    </TabList>
    <TabPanel Value="a">Crisp and sweet.</TabPanel>
    <TabPanel Value="b">Long and yellow.</TabPanel>
</Tabs>
```

The root creates the shared state and provides it; the parts use it:

```csharp
internal sealed class TabsState
{
    public static readonly Context<TabsState> Context = new("Primitives.Kit.Tabs");
    // the selected value, the registered tabs in build order, ids from UseId…
}

// Tabs.Build
var state = new TabsState(UseId("tabs"), Value ?? new Signal<string>(DefaultValue), Vertical, OnValueChange);
Provide(TabsState.Context, state);

// Tab.Build
var state = Use(TabsState.Context);
var value = Value;
bool Selected() => state.Selected.Value == value;
Element button = null!;
button = Button(
    Id(state.TabId(value)),
    Attr("role", "tab"),
    Attr("aria-controls", state.PanelId(value)),
    Attr("aria-selected", () => Selected() ? "true" : "false"),
    Attr("tabindex", () => Selected() ? "0" : "-1"),     // roving tabindex
    Attr("data-state", () => Selected() ? "active" : "inactive"),
    Splat(Attributes),
    OnClick(() => state.Select(value)),
    OnKeyDown(e =>
    {
        var to = RovingFocus.Move(e.Key, state.IndexOf(value), state.Count, state.Vertical);
        if (to >= 0) { state.Select(state.ValueAt(to)); state.ElementAt(to).Focus(); }
    }),
    ChildContent is null ? Spread() : ChildContent);
state.Register(value, button);
OnCleanup(() => state.Unregister(value));
```

- **Ids come from the root.** One `UseId` in the root gives every part a stable prefix, and the
  parts derive `aria-controls` and `aria-labelledby` from the root's id and their `Value`. So
  `Value` ends up in element ids and must be id-safe.
- **Parts register themselves** in build order, which is document order for static markup, so
  arrow-key navigation follows what the user sees. `OnCleanup` unregisters a part that an `@if` or
  `@foreach` removes.
- **The context is internal.** A context is matched by the object, not the name, so two libraries'
  tabs nested in each other never see each other's state. Put the library in the name anyway: it
  is what the error message shows.
- **A part outside its root throws** when it calls `Use`: `No value provided for context
  'Primitives.Kit.Tabs'`. Say which root a part needs in its doc comment.

## Example: a focus scope

`samples/Primitives.Kit/FocusScope.cs` keeps focus inside a non-modal panel. It needs no JavaScript:

```csharp
var previous = 0;
OnMount(() =>
{
    previous = ActiveElementId();   // who had focus before
    content.FocusFirst();           // move it into the panel
});
OnCleanup(() => Html.Focus(previous));   // give it back when the panel goes away

// Two invisible sentinels around the content send Tab round to the other end,
// and a focusin outside the panel pulls focus back in.
var root = Div(Style("display:contents"),
    Sentinel(() => content.FocusLast()), content, Sentinel(() => content.FocusFirst()));
OnDocument("focusin", e => { if (!e.Inside) content.FocusFirst(); }, within: root);
```

`RovingFocus.Move(key, from, count, vertical)` in the same folder is the arrow-key logic that tabs,
menus and toolbars share; `samples/Primitives.Kit/Tabs.cs` uses it to move the selection and `Focus()`.

## State for styling

Publish state as attributes, not classes, so every design system can select on the same names:

```html
<button aria-expanded="true" data-state="open">…</button>
<div role="menu" data-state="open" data-side="bottom">…</div>
```

A design system then styles `[data-state=open]` and `[aria-selected=true]`, and content with
`AnimateExit` gets `data-state="closed"` while it animates out.

Use the same names across components: `data-state` is `open`/`closed` for things that expand and
`active`/`inactive` for things that are selected, `data-disabled` is present or absent, and
`data-orientation` is `horizontal` or `vertical`. Put the attribute on every part a design system
may style (the `Disclosure` puts `data-state` on its root, trigger and panel), so CSS does not
need to reach across the tree.

## Patterns

The [WAI-ARIA Authoring Practices](https://www.w3.org/WAI/ARIA/apg/patterns/) describe the roles,
states and keys of each widget; follow them, and test with a screen reader.

**Disclosure and accordion.** A `button` with `aria-expanded` and `aria-controls`, and a panel
that is `hidden` while closed (`samples/Primitives.Kit/Disclosure.cs`). An accordion is a list of
disclosures; with "one open at a time", make it a compound component whose root keeps the open
value, as `Tabs` does.

**Modal dialog.** Build on `<dialog>` and `showModal()`: the browser traps focus, closes on
Escape, makes the rest of the page inert and gives focus back on close.
`samples/Primitives/Dialog.cs` drives it from a signal and writes the signal back from the
dialog's `close` event:

```csharp
var dialog = El("dialog", Attr("aria-labelledby", titleId), Splat(Attributes),
    On("close", () => Open.Value = false), /* title, content */);
var mounted = new Signal<bool>(false);
OnMount(() => mounted.Value = true);   // showModal() needs the dialog in the document
new Effect(() =>
{
    if (!mounted.Value) return;
    if (Open.Value) dialog.ShowModal(); else dialog.Close();
});
```

**Popover, menu, listbox.** Two ways:

- The `popover` attribute: the browser puts the panel in the top layer, closes it on Escape and
  an outside click, and a button with `popovertarget` opens it without any C#. Listen to the
  panel's `toggle` event (`e.NewState` is `"open"` or `"closed"`) to keep a signal in sync and to
  call `FocusFirst()` when it opens.
- A panel you show yourself, through `Portal("body", panel)` so `overflow: hidden` cannot clip it
  (`samples/Primitives/Popover.cs`). Then close it yourself: in an effect that runs while it is
  open, `OnDocument("pointerdown", …, within: panel)` with `e.Inside`, and `OnDocument("keydown", …)`
  for Escape, which also gives focus back to the trigger. The listeners are removed when the
  effect runs again or the component goes away.

The items of a menu or listbox use roving focus like `Tab`: one item has `tabindex="0"`, the
others `-1`, and `RovingFocus.Move` picks the next one. A menu item has `role="menuitem"`, the
trigger `aria-haspopup="menu"` and `aria-expanded`. Position the panel with CSS anchor
positioning where it is supported, or read `trigger.GetRect()` when it opens, as `Popover` does.

**Non-modal panel that keeps focus.** `FocusScope` above.

## Accessibility checklist

Before a component ships:

- [ ] It has the role, states and properties of its [APG pattern](https://www.w3.org/WAI/ARIA/apg/patterns/),
      and every state that changes (`aria-expanded`, `aria-selected`, `aria-checked`) is bound to
      the signal, not set once.
- [ ] It works with the keyboard alone: Tab reaches it once, the pattern's keys work, and Escape
      closes anything that floats.
- [ ] Focus moves where the pattern says when something opens, and goes back to the trigger when
      it closes.
- [ ] Every `aria-labelledby`, `aria-controls` and `for` points at an id from `UseId`, so two
      instances on one page do not clash.
- [ ] Buttons are `<button type="button">`, links are `<a href>`; no clickable `div`s.
- [ ] Hidden content is `hidden` (or removed), not only moved off screen, so a screen reader skips it.
- [ ] It does not depend on colour or animation to show state; `data-state` and ARIA carry it.
- [ ] A caller can add a label: `aria-label` and `aria-labelledby` from the tag reach the element
      that needs them.

## Pitfalls

- **`Provide` and `Use` only work while building.** Call `Use` in `Build` and keep the result in a
  local; an event handler, effect or `OnMount` has no scope to look in, and `Use` throws.
- **DOM calls need the DOM.** `ShowModal()`, `Focus()` and `GetRect()` act on the element in the
  page, which exists after the first flush. Call them from `OnMount`, an effect that waits for it,
  or an event handler, never directly in `Build`.
- **`preventDefault` is a flag, not a call.** `ListenerFlags.PreventDefault` (and
  `@onkeydown:preventDefault`) applies to every event the listener gets, because the browser
  decides before C# runs. A keydown listener with it also blocks Tab and typing; leave it off
  arrow-key handlers.
- **Read props once.** `Build` runs once per instance. Copy a plain prop such as `Value` into a
  local before a lambda uses it, and make a prop a `Signal<T>` or `Prop<T>` if it must change later.
- **Ids from values.** A part that puts its `Value` in an element id (like `Tab`) needs an id-safe
  value; say so in the doc comment, or map values to indexes.
- **Wasm size.** A library is compiled into every app that uses it. Avoid APIs that pull in large
  tables, such as `ToLowerInvariant`, `char.IsLetter` and `StringComparison.OrdinalIgnoreCase`;
  see [Keeping the Wasm small](deployment.md#keeping-the-wasm-small).

## Testing

Test behaviour with [`NwUi.Testing`](testing.md): render a component, click it or fire
`KeyDown("ArrowDown")`, and check `aria-selected`, `data-state` and `Focused`. For each component,
cover:

- the uncontrolled mode (`DefaultOpen`) and the controlled one (a signal the test writes and reads),
- that the change callback fires for user actions only,
- the keyboard: every key of the pattern, and wrapping at both ends,
- that `class`, `id` and an `@on…` handler from the tag land on the root.

`tests/NetWasm.UI.Tests/KitTests.cs` does this for the kit's `Disclosure`, `Tabs` and the theme's
`Expander`, and `dotnet new netwasm-ui-lib` creates a test project with the same setup. Test CSS,
layout, focus trapping by the browser and animations in a browser, as `tests/NetWasm.UI.E2E` does
with Playwright.
