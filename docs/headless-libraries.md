# Building a headless library

A headless library ships behaviour without looks: a dialog, a menu or tabs with the right
keyboard handling, focus moves and ARIA attributes, styled by the app or by a design-system
library on top. This page shows which NetWasm.UI pieces such a library is built from.
`samples/Primitives.Kit` is a small library built this way, and `samples/Primitives` uses it.
It targets `netstandard2.1`, so the same build runs in the browser and in `net10.0` tests; see
[Your own libraries](libraries.md#your-own-libraries) for the project file.

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
menus and toolbars share; `samples/Primitives/Tabs.cs` uses it to move the selection and `Focus()`.

## State for styling

Publish state as attributes, not classes, so every design system can select on the same names:

```html
<button aria-expanded="true" data-state="open">…</button>
<div role="menu" data-state="open" data-side="bottom">…</div>
```

A design system then styles `[data-state=open]` and `[aria-selected=true]`, and content with
`AnimateExit` gets `data-state="closed"` while it animates out.

## Testing

Test behaviour with [`NwUi.Testing`](testing.md): render a component, fire `KeyDown("ArrowDown")`,
and check `aria-selected` and `Focused`. Test CSS, layout and animations in a browser, as
`tests/NetWasm.UI.E2E` does with Playwright.
