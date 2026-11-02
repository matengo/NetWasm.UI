# Reactivity

State lives in signals. Reading a signal inside a reactive context subscribes to it; writing it
re-runs exactly the code that read it. There is no virtual DOM and no component re-render.

```csharp
using NetWasm.UI.Reactive;
```

## Signal

```csharp
var count = new Signal<int>(0);

count.Value++;                      // read + write
var now = count.Peek();             // read without subscribing
count.Update(c => c * 2);
items.Touch();                      // notify after mutating a list in place
var name = new Signal<string>("", StringComparer.OrdinalIgnoreCase);   // custom equality
```

A write that does not change the value (by the comparer, `EqualityComparer<T>.Default` if none
is given) notifies nobody.

## Computed

```csharp
var doubled = new Computed<int>(() => count.Value * 2);
Text(doubled)
```

A `Computed` caches a derived value. It recomputes when a dependency changes and only notifies
its own readers if the result changed. It recomputes eagerly, so in a diamond (`a → b, c → d`)
`d` may recompute once per branch; see [Benchmarks](benchmarks.md#still-open).

## Effect

```csharp
new Effect(() => Console.WriteLine("count is " + count.Value.ToString()));
```

An effect runs immediately and again after any signal it read changes. Element helpers such as
`Text(() => …)`, `Class(() => …)`, `When` and `Each` create their own effects, so app code rarely
needs `Effect` except for side effects (storage, timers, logging).

Inside an effect, `OnCleanup(() => …)` runs before the next run and when the effect is disposed.

Use `Tracker.Untracked(() => …)` to read signals inside an effect without subscribing.

## Selector

For "is this the selected row?" across many rows:

```csharp
var selected = new Signal<int>(-1);
var isSelected = new Selector<int>(selected);

Each(rows, r => r.Id, row =>
    Tr(Class(() => isSelected.Is(row.Id) ? "danger" : ""), …))
```

`isSelected.Is(key)` subscribes to that key only, so changing the selection re-runs two rows
instead of all of them (Solid's `createSelector`).

## Batching

Event handlers already run in a batch. Elsewhere (timers, JS callbacks), group writes so that
effects and the DOM update run once:

```csharp
Scheduler.Batch(() =>
{
    first.Value = "Ada";
    last.Value = "Lovelace";
});
```

After the outermost batch, the scheduler runs queued effects and the renderer sends **one**
binary op batch to the browser. `Scheduler.Flush()` forces this outside a batch.

## Scopes and ownership

Every effect, computed and component is registered with the current `Scope`. Disposing a scope
disposes everything it owns. Components, `When` branches, `Each` rows and routed pages each get
their own scope, so hiding a branch or removing a row tears down its effects and DOM listeners.

```csharp
var scope = new Scope();
var node = scope.Run(() => BuildSomething());
…
scope.Dispose();
```

You only need `Scope` directly when you create reactive things outside a component, for example
in tests.

## Context and stores

A store is a plain class with signals. Provide it once and use it anywhere below:

```csharp
public sealed class AppState
{
    public static readonly Context<AppState> Context = new("AppState");
    public Signal<int> Count { get; } = new(0);
}

// in the root component, while building:
Provide(AppState.Context, new AppState());

// in any descendant (also inside When/Each branches and routed pages), while building:
var count = Use(AppState.Context).Count;
```

- Contexts are keyed by the `Context<T>` instance, so there is no reflection.
- `Provide(ctx, value, () => subtree)` provides a value to one subtree only.
- `Use` throws if nothing was provided and the context has no default
  (`new Context<Theme>("Theme", Theme.Light)`).
- Call `Use` while building, not inside a reactive lambda. In templates, call it in `OnInit`.
- Pages are rebuilt on navigation. Keep state that should survive in a store
  (see `samples/Counter/AppState.cs`).

## Lifecycle

```csharp
protected override Node Build()
{
    var input = Input();
    OnMount(input.Focus);                    // after the browser has applied this update's DOM
    var timer = StartTimer();
    OnCleanup(() => timer.Dispose());        // when the component or branch is torn down
    return Div(input);
}
```

- `OnMount` runs once, in its own batch, after the host applied the DOM ops. It is skipped if
  its scope was disposed first.
- `OnCleanup` runs when the owning scope is disposed (or, inside an `Effect`, before each re-run).
