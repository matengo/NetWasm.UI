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
`PreventDefaultPlainClick` (prevent only an unmodified primary click; used by `Link`).

`DomEvent` carries `Value`, `Checked`, `Key`, `Repeat`, `Ctrl`/`Shift`/`Alt`/`Meta`, `Button`,
`ClientX`/`ClientY` and `IsPlainClick`. Each handler runs inside a scheduler batch, so a handler
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
- To change a list, assign a new list to the signal (`items.Value = newList`) or mutate and call
  `items.Touch()`.

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

## Spread

`Spread(params Content[])` groups several pieces of content into one, which is handy for
helpers that return attributes plus listeners (`Bind` is one):

```csharp
static Content Tooltip(string text) => Spread(Attr("title", text), Attr("aria-label", text));
```

## Examples

`samples/Counter/Components` builds the same room UI twice, once as a template
(`RoomRazor.nwui`) and once in C# (`RoomCode.cs`), from a template component with slots
(`Panel.nwui`), one with `@prop` and two-way binding (`Stepper.nwui`) and a C# component with
`Prop<T>` (`ProgressBar.cs`).
