# Error handling

An exception thrown inside the UI goes to the nearest `ErrorBoundary`. If there is none, it goes
to `App.OnError`. If that is not set either, it is rethrown once the current update has reached
the DOM, so it still shows up in the browser console.

## ErrorBoundary

An `ErrorBoundary` renders its content. When something inside the content throws, the content is
torn down (nodes, effects and listeners) and the fallback is rendered in its place.

In C#:

```csharp
using static NetWasm.UI.Html;

Div(
    ErrorBoundary(
        () => new Dashboard().Render(),
        error => P(
            "Something went wrong: " + error.Message,
            Button("Retry", OnClick(_ => error.Reset())))));
```

In a template, put the content and a `<template slot="Fallback">` inside `<ErrorBoundary>`.
`context` names the `BoundaryError` variable (default `error`):

```razor
<section>
    <ErrorBoundary>
        <Dashboard />
        <template slot="Fallback" context="error">
            <p>Something went wrong: @error.Message
               <button @onclick="() => error.Reset()">Retry</button></p>
        </template>
    </ErrorBoundary>
</section>
```

Rules:

- The content and the fallback must each have one root node.
- `<ErrorBoundary>` takes no attributes, and it cannot be the only content of a template or an
  `@if`/`@foreach` block. Wrap it in an element, like `@if`.

`BoundaryError` has:

| Member | Meaning |
|---|---|
| `Exception` | The exception that was caught |
| `Message` | `Exception.Message` |
| `Reset()` | Clears the error and builds the content again, with fresh state |

## What is caught

| Where the exception is thrown | Where it goes |
|---|---|
| While building the content (components, `Build`, templates) | The enclosing boundary |
| An effect, `Text(() => …)`, reactive attribute, `When`/`Each` re-running later | The boundary around where the effect was created |
| An event handler | The boundary around the element with the listener |
| An `OnMount` callback | The boundary around the component that registered it |
| A `SetInterval` tick | The boundary around the component that started the timer |
| An `OnCleanup` callback or effect cleanup | `App.OnError` (the boundary may already be gone) |
| The fallback itself | The next boundary out |
| The initial `App.Mount` with no boundary | `App.OnError` |

DOM changes made before the exception are still applied. For example, an event handler that
updates a signal and then throws still updates the page.

Code that runs outside the framework is not caught. That includes your own `JSImport`
callbacks and timers that do not go through an event handler. See
[Callbacks from JavaScript](js-interop.md#callbacks-from-javascript).

## Global hook

`App.OnError` receives every error that no boundary handled. Set it before `App.Mount` or
`BrowserApp.Start`:

```csharp
App.OnError = ex => errorMessage.Value = ex.Message;
BrowserApp.Start(new AppRoot());
```

`OnError` is a single `Action<Exception>`, not an event. Setting it replaces the previous
handler.

Without a hook, the first unhandled error is rethrown after the update has been flushed to the
DOM. The page stays consistent and the error shows up in the browser console.

## Size

The error routing in the runtime costs about 3 KB raw (under 1 KB Brotli) in the Wasm module.
Using `ErrorBoundary` and showing `Exception.Message` adds more, about 14 KB raw (2 KB
Brotli) in the demo app.
