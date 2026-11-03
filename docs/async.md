# Async data and Suspense

NetWasm compiles `async`/`await` and `Task`, and `HttpClient` runs on the browser's `fetch`.
NetWasm.UI turns a task into reactive state with `Resource<T>`, and shows a placeholder while
data loads with `Suspense`. `samples/Fetch` is the complete example.

## Resource

```csharp
// Loads once, when the component is built:
users = Resource.Create(ct => api.GetUsersAsync(ct));

// Loads again whenever the source changes; loads nothing while the source is null:
details = Resource.Create(() => selected.Value == 0 ? (int?)null : selected.Value,
                          (id, ct) => api.GetDetailsAsync(id, ct));
```

Each of these members is a tracked read, so a binding that uses it updates when a load finishes:

| Member | Meaning |
|---|---|
| `State` | `Unresolved`, `Pending`, `Ready`, `Refreshing` or `Errored` |
| `Value` | The loaded value. Throws when the load failed, so the error reaches the nearest `ErrorBoundary`. |
| `Latest` | The last successful value. It never throws, and it keeps the old value while a reload runs. |
| `Loading` | True while a load runs |
| `Error` | The exception from the last load, or `null` |
| `Refetch()` | Loads again |
| `Mutate(value)` | Sets the value locally (an optimistic update) and cancels a running load |

The resource belongs to the scope it is created in, usually `OnInit` or `Build`. When the
component is removed, a running load is cancelled through its `CancellationToken`. When the
source changes during a load, that load is cancelled and its result is ignored. Pass the token
on to `HttpClient`.

```razor
<p>@(details.Loading ? "Loading…" : details.Error is { } e ? "Failed: " + e.Message : details.Latest?.Bio)</p>
```

## Suspense

`Suspense` shows its fallback until every resource created inside it has finished its first
load. Then it inserts the content in one step. Later reloads keep the content on screen; use
`Loading` to show progress inline.

```razor
<ErrorBoundary>
    <div>
    <Suspense>
        <UserList Api="@Api" />
        <template slot="Fallback"><p>Loading users…</p></template>
    </Suspense>
    </div>
    <template slot="Fallback" context="error">
        <p>Could not load: @error.Message <button @onclick="() => error.Reset()">Retry</button></p>
    </template>
</ErrorBoundary>
```

In C#: `Suspense(() => new UserList { Api = api }.Render(), () => P("Loading…"))`.

- Create the resource inside the content, for example in the child's `OnInit`. A resource
  created outside does not hold the fallback.
- A failed load shows up when the content reads `Value`. Wrap `Suspense` in an `ErrorBoundary`,
  as shown above, to show the error and offer a retry. See [Error handling](error-handling.md).
- `OnMount` callbacks inside the content run while it is still detached. For focus or
  measurement, use an effect on the resource instead.

## HttpClient and JSON

- Reference `NetWasm.System.Net.Http` and `NetWasm.System.Text.Json`, not the Microsoft packages.
- Use a source-generated `JsonSerializerContext`: NetWasm has no reflection-based serializer.
- Give `HttpClient` an absolute `BaseAddress`. `samples/Fetch` passes `location.origin` from
  `app.mjs` to its `start` export.

## Size

Async adds to the Wasm module:

- The first use of `async`/`Task` adds about 15–20 KB with Brotli: the task machinery is
  compiled in.
- `HttpClient` adds about 180 KB.
- `System.Text.Json` adds about 200 KB.

Prefer a small hand-written API layer to large generic clients when size matters. See
[Publishing and size](deployment.md).
