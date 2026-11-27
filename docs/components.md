# Components and the C# API

Everything in NetWasm.UI is built from the static methods on `NetWasm.UI.Html`. `.nwui`
[templates](templates.md) compile to exactly these calls, so this page describes both.

```csharp
using NetWasm.UI;
using NetWasm.UI.Reactive;
using static NetWasm.UI.Html;
```

## Components

```csharp
public sealed class CounterView : Component
{
    private readonly Signal<int> count = new(0);

    protected override Node Build()
    {
        var doubled = new Computed<int>(() => count.Value * 2);

        return Section(
            H2("Counter"),
            P("Count: ", Strong(Text(count)), " · doubled: ", Text(doubled)),
            Button(OnClick(() => count.Value++), "+1"),
            Button(Disabled(() => count.Value == 0), OnClick(() => count.Value = 0), "Reset"),
            When(() => count.Value >= 10, () => P("That's a lot of clicks!")));
    }
}
```

- `Build()` runs **exactly once**, untracked, inside the component's own `Scope`. It is not a
  render function: nothing re-runs it. Updates come from reactive pieces (`Text(() => …)`,
  `Class(() => …)`, `When`, `Each`, …) that each own a small effect.
- A component can be used anywhere content is expected: `Div(new CounterView())`.
- `Render()` builds it (once) and returns the root node; `Root` is that node; `Dispose()`
  removes it from the DOM and disposes everything created in its scope.

### Static components

A piece of UI without state doesn't need a class. A plain method that returns a `Node` works
anywhere content is expected:

```csharp
private static Node Card(string title, string body) =>
    Div(Class("card"),
        H3(title),
        P(body));

Section(
    Card("Fast", "Signals update only what changed."),
    Card("Small", "About 150 KB Brotli for the demo app."));
```

It runs once when it is called and has no scope of its own. Its reactive parts, such as
`Text(() => …)`, belong to the component that called it. Use a `Component` when you need
signals that are local to the component, lifecycle hooks or `Dispose()`.

## Elements and content

Element functions take `params Content[]`. `Content` converts implicitly from `string`, `Node`,
`Attr`, `Listener`, `Component` and fragments (`When`, `Each`, `Slot`, `Routes`), so attributes,
events and children can be mixed in any order:

```csharp
Div(Class("card"), H3("Title"), P("Body"), Button(OnClick(Save), "Save"))
```

Elements: `Div`, `Span`, `P`, `H1`–`H3`, `Ul`, `Ol`, `Li`, `Button`, `Input`, `Label`, `Form`,
`Section`, `Header`, `Footer`, `Main`, `Nav`, `A`, `Strong`, `Em`, `Small`, `Table`, `Thead`,
`Tbody`, `Tr`, `Th`, `Td`, `Select`, `Option`, `Textarea`, `Pre`, `Code`, `Img`, `Br`, `Hr`.
Anything else: `El("article", …)`.

SVG elements are made with `SvgEl`, the `<svg>` element and everything inside it:
`SvgEl("svg", Attr("viewBox", "0 0 10 10"), SvgEl("circle", Attr("r", "4")))`. An element made with
`El` is an HTML element, which the browser does not draw inside an `<svg>`.

An `Element` you keep a reference to can be focused (`input.Focus()`) or given more content
later (`el.Add(...)`).

## Text

| Call | Updates when |
|---|---|
| `"literal"` or `Text("literal")` | never |
| `Text(signal)` | the signal or computed changes |
| `Text(() => a.Value + " " + b.Value)` | any signal read in the lambda changes |
| `Text(() => count.Value)` | same; numbers, `bool` and strings are formatted without boxing, `null` prints nothing |

## Attributes and properties

| Call | Notes |
|---|---|
| `Attr("data-x", "1")`, `Attr("aria-label", () => …)` | static or reactive attribute; returning `null` removes it |
| `Class(…)`, `Style(…)` | string or `Func<string?>` |
| `Id`, `Type`, `Href`, `Src`, `Alt`, `Placeholder`, `Title` | static attributes |
| `Disabled(bool)`, `Disabled(() => …)` | toggled attribute |
| `Autofocus()` | |
| `Value("…")`, `Value(() => …)` | sets the `value` **property** (what the user sees), not the attribute |
| `Checked(() => …)` | sets the `checked` property |

Reactive attributes only send an op when the value actually changed.

## Events

```csharp
Button(OnClick(() => count.Value++), "+1")
Input(OnInput(e => name.Value = e.Value))
Input(OnKeyDown(e => { if (e.Key == "Enter") Add(); }))
Form(OnSubmit(_ => Save()), …)                       // OnSubmit prevents the default submit
Div(On("pointerdown", e => Start(e.ClientX, e.ClientY), ListenerFlags.StopPropagation))
```

Helpers: `OnClick`, `OnInput`, `OnChange`, `OnKeyDown`, `OnSubmit`, `OnDblClick`, `OnBlur`, and
`On(name, handler, flags)` for any event. `ListenerFlags` (in `NetWasm.UI.Dom`): `PreventDefault`, `StopPropagation`,
`PreventDefaultPlainClick` (prevent only an unmodified primary click; used by `Link`), `Capture` (run in
the capture phase, before listeners below) and `Passive` (promise not to call `preventDefault`, for
`touchmove`, `wheel` and `scroll`). `Capture` and `Passive` listeners are added to the element
itself instead of being delegated; `Passive` with `PreventDefault` throws.

`DomEvent` carries `Value`, `Checked`, `Key`, `Repeat`, `Ctrl`/`Shift`/`Alt`/`Meta`, `Button`,
`ClientX`/`ClientY` and `IsPlainClick`. For overlays it also has `TargetId` and `RelatedTargetId`
(the ids of the framework elements involved, 0 otherwise; test with `e.IsTarget(element)`),
`Inside`/`RelatedInside` (see `within` under [Building component libraries](#building-component-libraries))
and `NewState` (`"open"`/`"closed"` for `toggle` events of `popover` and `<details>`). Each handler runs inside a scheduler batch, so a handler
that writes ten signals still produces one DOM update.

## Two-way binding

`Bind` returns the `value`/`checked` property plus the matching listener; place it with `Spread`:

```csharp
var name = new Signal<string>("");
var done = new Signal<bool>(false);

Input(Placeholder("Name"), Spread(Bind(name)))
Input(Type("checkbox"), Spread(Bind(done)))
```

## Conditionals and lists

```csharp
When(() => user.Value is null,
     () => P("Signed out"),
     () => P("Hello ", Text(() => user.Value!.Name)))   // the else branch is optional

Ul(Each(items, i => i.Id, item =>
    Li(Class(() => item.Done.Value ? "todo done" : "todo"),
       Label(Input(Type("checkbox"), Spread(Bind(item.Done))), " ", item.Title),
       Button(OnClick(() => Remove(item)), "×"))))
```

- `When` only rebuilds when the condition flips. The branch gets its own scope and is disposed
  when it is hidden.
- `Each(items, key, render)` takes a `Func<IReadOnlyList<T>>` or an
  `IReadOnlySignal<IReadOnlyList<T>>`. Rows are keyed: a row is built once per key and moved, not
  rebuilt, when the list is reordered (a longest-increasing-subsequence diff gives the minimum
  number of moves). Put per-row state that changes in signals on the item (`item.Done`).
- A row shows its key's item. When a new list has a different item for a key that stays (another
  object, or for a value type a value that is not equal), that row is disposed and rendered again
  for the new item, in the same place, and its local state starts over. Keep the same object to
  keep the row.
- To change a list, assign a new list to the signal (`items.Value = newList`) or mutate and call
  `items.Touch()`.

### Exit animations

`When(…).AnimateExit()` and `Each(…).AnimateExit()` (`@if (…) animate-exit` and
`@foreach (…) animate-exit` in templates) let removed content animate out before it leaves the
DOM. Shown content and new rows get `data-state="open"`. When a branch is hidden or a row
removed, the element gets `data-state="closed"` and `inert`, and is removed once its CSS
animations and transitions, and those of its descendants, have finished. With none, it is removed
at once. The animation is plain CSS:

```css
.toast { transition: opacity .2s, translate .2s; }
@starting-style { .toast { opacity: 0; translate: 0 8px; } }      /* entering */
.toast[data-state=closed] { animation: fade-out .2s forwards; }   /* leaving */
@keyframes fade-out { to { opacity: 0; } }
```

The C# side is done with the content at once: its scope is disposed and a new branch or row can
render beside it while it fades. A row that is rebuilt for a new item (see above) does not
animate. `data-state` goes on the root element of the branch or row. A `<dialog>` or `popover` element needs no `AnimateExit`: it stays in the DOM, so
animate it with `transition-behavior: allow-discrete` on `display` and `overlay`:

```css
dialog { opacity: 0; transition: opacity .2s, display .2s allow-discrete, overlay .2s allow-discrete; }
dialog[open] { opacity: 1; }
@starting-style { dialog[open] { opacity: 0; } }
```

## Static templates (fast rows)

`Div(…)`, `Td(…)` and friends send one op per element, attribute and text. For markup that is
created many times (list rows, cards), parse the static part once with a `Template` and clone it:

```csharp
static readonly Template RowTemplate = new(
    "<tr><td class=\"col-md-1\"><!--$--></td>" +
    "<td class=\"col-md-4\"><a $><!--$--></a></td>" +
    "<td><a $><span class=\"remove\">×</span></a></td><!--@--></tr>");

Node Row(Item item) => RowTemplate.Clone()
    .With(0, Class(() => selected.Value == item.Id ? "danger" : null))   // hole 0 = root
    .Text(1, item.Id.ToString())                                          // <!--$-->
    .With(2, OnClick(() => Select(item)))                                // <a $>
    .Text(3, () => item.Label.Value)                                     // reactive text
    .With(4, OnClick(() => Remove(item)))
    .With(5, When(() => item.Open.Value, () => Td("details")))          // <!--@-->
    .Root;
```

Holes are numbered in document order; the root element is always hole 0:

| Marker | Hole | Fill with |
|---|---|---|
| root element, or an element with a bare `$` attribute | element | `With(i, attrs, events, children…)`, `Ref(i, out el)`, `Element(i)` |
| `<!--$-->` | text node | `Text(i, "…")`, `Text(i, () => …)`, `Text(i, () => number)` |
| `<!--@-->` | insertion point | `With(i, …)`: content is inserted before the marker (`When`, `Each`, components, nodes) |

The HTML must have one root element. Whitespace-only text that contains a line break is
dropped, as in templates. The template is sent to the browser once and then cloned with
`cloneNode`, so creating a row costs one op plus one per hole instead of one per node.
`.nwui` templates do this automatically.

Markup that is cloned into an `<svg>`, such as a row of circles, is SVG content: create it with
`Template.Svg("<circle $ r=\"1\"></circle>")`, so its elements are SVG elements.

## Events and delegation

Common bubbling events (`click`, `input`, `change`, `keydown`, `submit`, pointer/mouse up/down,
`focusin`/`focusout`, …) are delegated: `dom.mjs` has one listener per event type on `document`
and walks up from the target. `StopPropagation` stops that walk. Because the delegated handler
runs at the document, a native listener you add yourself on an ancestor runs **before** it.
Other events (`blur`, `focus`, `scroll`, `mouseenter`, …) get a listener on the element.

## Props

Plain properties are read once when the parent builds:

```csharp
public sealed class Badge : Component
{
    public string Label { get; set; } = "";
    protected override Node Build() => Span(Class("badge"), Label);
}

Div(new Badge { Label = "new" })
```

For a value the child should follow, use `Prop<T>`. It holds either a fixed value or a getter,
and reading `.Value` inside a reactive context tracks whatever the getter reads:

```csharp
public sealed class ProgressBar : Component
{
    public Prop<int> Value { get; set; } = 0;
    public Prop<int> Max { get; set; } = 100;

    protected override Node Build() =>
        Div(Class("progress"),
            Span(Text(() => Value.Value.ToString() + " / " + Max.Value.ToString())));
}

new ProgressBar { Value = done }                          // Signal<int> or Computed<int>
new ProgressBar { Value = new(() => done.Value * 10) }    // any expression
new ProgressBar { Value = 3 }                             // a fixed value
```

Reading `Value.Value` directly in `Build()` gives a snapshot, because `Build` is untracked.

For two-way state, pass the `Signal<T>` itself: `public Signal<int> Count { get; set; } = new(0);`.

## Child content (slots)

A `Slot` is content the parent supplies and the child places:

```csharp
public sealed class Card : Component
{
    public Slot? Actions { get; set; }
    public Slot? ChildContent { get; set; }

    protected override Node Build() =>
        Section(Class("card"),
            Div(Class("actions"), Actions),     // an unset slot renders nothing
            Div(Class("body"), ChildContent));
}

new Card
{
    Actions = new Slot(() => Button(OnClick(Reset), "Reset")),
    ChildContent = new Slot(() => P("Body ", Text(count))),
}
```

The slot is built where the child places it, so it lives in the child's scope (and is disposed
with it) but still reads the parent's signals.

A `Slot<T>` takes an argument, for per-item templates such as table cells. Place it with
`slot.With(arg)`; in a template, `@render(slot, arg)`:

```csharp
public Slot<Person>? Cell { get; set; }

Td(Cell?.With(person))

new Table { Cell = new(p => Span(p.Name)) }
```

## Spread

`Spread(params Content[])` groups several pieces of content into one, which is handy for
helpers that return attributes plus listeners (`Bind` is one):

```csharp
static Content Tooltip(string text) => Spread(Attr("title", text), Attr("aria-label", text));
```

## Attributes

Every component has `Attributes` (`Content[]`), which templates fill with the lowercase or dashed
attributes and `@on…` handlers given on its tag (see
[Attribute splatting](templates.md#attribute-splatting)). Apply them with `Splat`, which joins
`class`/`style` with the given base value and attaches a fresh listener each time it is applied:

```csharp
protected override Node Build() =>
    Button(Attr("type", "button"), Splat(Attributes, baseClass: "btn"), Text(() => Label.Value));
```

The caller sets them like any other property:

```csharp
new MyButton { Label = label, Attributes = [Class("wide"), Attr("aria-label", "Save"), OnClick(Save)] }
```

## Building component libraries

The core has no dialog, popover or select. It has the pieces to build them in a separate
library; `samples/Primitives` (a dialog, popover, tabs, select and data table) uses all of them.

**Element calls.** `Element.Call(method, args…)` calls a method on the DOM element, after the
pending DOM updates, with string arguments. Helpers: `ShowModal()`, `Show()`, `Close(returnValue)`,
`ShowPopover()`, `HidePopover()`, `TogglePopover()`, `Focus()`, `Blur()`, `Select()` and
`ScrollIntoView(block)`. Call them from an event handler, an effect or `OnMount`:

```csharp
var dialog = El("dialog", On("close", () => open.Value = false), content);
new Effect(() => { if (open.Value) dialog.ShowModal(); else dialog.Close(); });
```

`showModal()` needs the element in the document, so if the dialog can start open, wait for
`OnMount` first (see `samples/Primitives/Dialog.cs`). For anything else, such as a JS library,
`BrowserDom.GetHandle(element)` (in `NetWasm.UI.Browser`) returns the element as a `JSObject`.

**Document and window listeners.** `OnDocument(event, handler, flags, within)` and
`OnWindow(…)` listen outside the component and are removed with the current component or effect
run. Inside an effect, that gives "listen only while open". With `within`, the handler gets
`e.Inside` (the target is inside that element) and `e.RelatedInside`:

```csharp
new Effect(() =>
{
    if (!open.Value) return;
    OnDocument("pointerdown", e => { if (!e.Inside) open.Value = false; }, within: panel);
    OnDocument("keydown", e => { if (e.Key == "Escape") { open.Value = false; trigger.Focus(); } });
});
```

Outside a component or effect (in an event handler), they return an `IDisposable` that you
dispose yourself. `OnResize(element, rect => …)` observes an element's size the same way.

**DOM reads.** `element.GetRect()`, `element.Contains(other)`, `element.HasFocus()`,
`ActiveElementId()` and `Viewport()` read the live DOM synchronously. They apply the pending DOM
updates first. Call them from event handlers or `OnMount`, not while building or inside an effect
(they throw there, because a read would make the effect depend on layout). Under a host without
reads (for example the prerenderer's) they throw `NotSupportedException`; prerendering returns
zeros.

**Focus.** `element.FocusFirst()` and `FocusLast()` focus the first or last tabbable element
inside `element` (the element itself when there is none), skipping disabled, hidden, `inert`,
invisible and `tabindex="-1"` elements. They run after the pending DOM updates, so call them
from `OnMount` to focus a panel that was just rendered. `Html.Focus(id)` focuses an element by
the id `ActiveElementId()` returned, to give focus back when a panel closes. For a modal, use
`<dialog>.showModal()`, which traps and restores focus natively. A focus scope with a trap and
roving tabindex are library components built on these; see `NwUi.Primitives`
(`FocusScope`, `RovingFocus`) and [Building a headless library](headless-libraries.md).

**Portal.** `Portal(target, content…)` (or `<Portal Target="body">…</Portal>` in a template)
renders content at the end of the element matching the CSS selector `target`, inside a
`<div data-nwui-portal style="display:contents">`. Use it for toasts and menus that must escape
`overflow: hidden` or a stacking context. The content still belongs to the component: context,
listeners, cleanup and error boundaries work as usual, and it is removed with the component.
A modal `<dialog>` and a `popover` element already render in the top layer and need no portal.

**Ids.** `UseId(prefix)` returns an id that is unique in the app (`"nw7"`), for `aria-labelledby`,
`aria-controls` and `for`. Ids come from a counter, so the same tree built in the same order
(as prerendering and the client do) gets the same ids.

**Generic components.** `@typeparam`, `Slot<T>`, `context=` and `@bind-Prop`: see
[Generic components and slots with parameters](templates.md#generic-components-and-slots-with-parameters).

## Examples

`samples/Counter/Components` builds the same room UI twice, once as a template
(`RoomRazor.nwui`) and once in C# (`RoomCode.cs`), from a template component with slots
(`Panel.nwui`), one with `@prop` and two-way binding (`Stepper.nwui`) and a C# component with
`Prop<T>` (`ProgressBar.cs`).

The [attribute splatting example](https://matengo.github.io/NetWasm.UI/examples/splatting)
(`site/Demos/SplatDemo.nwui`) has a button component that takes `class`, `type`, `disabled`,
`aria-*` and `@onclick` from its tag, an icon button that forwards them with `@attributes`, and a
text field that takes `placeholder` and `@onkeydown`.
