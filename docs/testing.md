# Testing components

> **Preview.** The `NwUi.Testing` package is new in this release. Its API may change in any 0.x
> version.

`NwUi.Testing` renders components in a normal .NET test process, without a browser. It runs the
real renderer against an in-memory DOM, so you can find elements, fire events and check the
markup from xUnit, NUnit or MSTest. It suits headless component libraries: keyboard handling,
ARIA attributes, focus moves and outside clicks are all testable this way.

## Setup

The test project is a plain `net10.0` project:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="NwUi.Testing" Version="..." />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.1" />
  </ItemGroup>
</Project>
```

The code under test must be usable from `net10.0`. A component library that targets
`netstandard2.1` (see [Your own libraries](libraries.md#your-own-libraries)) is: reference it from
the test project. A `net10.0-netwasm` library is not; put such components in the test project instead
(`.nwui` files compile there as well).

## Writing a test

```csharp
using NetWasm.UI.Testing;

public class CounterTests
{
    [Fact]
    public void Clicking_increments()
    {
        using var r = TestRenderer.Render(new Counter());

        r.Click(r.Find("button.increment"));

        Assert.Equal("Count: 1", r.Find("output").Text);
    }
}
```

`TestRenderer.Render(component, services)` mounts the component and returns the renderer.
Dispose it at the end of the test. Updates are synchronous: after `Click`, `Input` or a signal
write the DOM is already up to date.

| Member | What it does |
|---|---|
| `Find(selector)`, `FindAll(selector)` | Query the rendered DOM. `Find` throws, with the current HTML, if nothing matches |
| `Html` | The markup of the whole app |
| `Focused` | The focused element, or `null` |
| `Click`, `Input`, `Change`, `KeyDown`, `Focus` | Fire the usual events. `Input` and `Change` also set the element's `value` |
| `Dispatch(element, DomEvent)` | Fire any event, such as `pointerdown` or `focusout` |
| `DispatchDocument`, `DispatchWindow` | Fire an event at a `Html.OnDocument` or `Html.OnWindow` listener directly |
| `SetRect(element, rect)`, `Viewport` | Set what `Measure` returns and notify `Html.OnResize` observers |

A `TestElement` has `Tag`, `NamespaceUri`, `Text`, `Html`, `Value`, `Attributes`, `GetAttribute`,
`GetProperty`, `Children`, `Parent`, `IsConnected` and its own `Find` and `FindAll`. Elements
compare equal when they wrap the same node, so `Assert.Equal(r.Find("#a"), r.Focused)` works.

Events follow the browser: capture listeners run from the root down, then the target, then
bubbling listeners up to the root, and `ListenerFlags.StopPropagation` stops it. After the
element listeners, document and window listeners run, and a listener registered with `within`
gets `Inside` set like in the browser.

## Selectors

Selectors are a small subset of CSS:

- a tag (`button`), `*`, an id (`#save`), a class (`.active`)
- attributes: `[disabled]`, `[role=dialog]`, `[data-id="2"]`
- compounds of these (`li.active[aria-selected=true]`) and the descendant combinator (`ul li`)

Anything else, such as `>`, `:focus` or a selector list, throws a `FormatException`.

## Limits

The DOM is a model, not a browser:

- There is no CSS or layout. `Measure` returns what `SetRect` set, or an empty rect.
- Focus is tracked, but `focusin` and `focusout` are fired only by `Focus`. `FocusFirst` and
  `FocusLast` pick the first or last element that is focusable by tag, `href` or `tabindex`,
  and is not disabled or `inert`. They ignore visibility.
- Animations do not run. A block with `animate-exit` is removed at once.
- Default actions do not happen: a click on a link does not navigate and a click on a submit
  button does not submit the form.

Only one `TestRenderer` can be live at a time because the renderer is global. A second
`Render` waits until the first one is disposed, so tests in different classes can run in
parallel without interfering. For what needs a real browser, such as CSS, layout and
animations, use end-to-end tests. This repository's own end-to-end tests use Playwright, in
`tests/NetWasm.UI.E2E`.
