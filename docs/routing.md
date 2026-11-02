# Routing

```csharp
protected override Node Build() =>
    Div(
        Nav(
            NavLink("/", "active", "Home"),
            NavLink("/todos", "active", "Todos")),
        Routes(
            NotFound,                                       // optional fallback
            Route("/", Home),
            Route("/todos", () => new TodoList().Render()),
            Route("/todos/{id}", p => new TodoPage(p.GetInt("id")).Render()),
            Route("/files/{*path}", p => Pre(p.Get("path")))));

private static Node NotFound() => P("Not found");
```

## Routes and patterns

- `{name}` matches one path segment; `{*name}` matches the rest of the path.
- Routes are tried in order; the first match wins. Without a match, the `notFound` function is
  rendered (or nothing).
- `Routes` rebuilds a page in a fresh `Scope` only when the matched route or its parameters
  change. A query or hash change alone does not rebuild the page.
- Pages are disposed when you navigate away. Keep state that should survive in a store provided
  via [context](reactivity.md#context-and-stores).

## Route parameters

`RouteParams` (the `p` above):

| Member | Returns |
|---|---|
| `p.Get("id")` | the decoded segment; throws if the pattern has no such parameter |
| `p["id"]` | the decoded segment, or `null` |
| `p.GetInt("id", fallback: 0)` | the segment as `int`, or the fallback |
| `p.Query` | the raw query string without `?` (empty if none) |

## Links and navigation

- `Link(href, …)` renders an `<a>` and navigates with the History API on a plain click.
  Ctrl/shift/meta/middle clicks reach the browser untouched, so "open in new tab" works.
- `NavLink(href, activeClass, …)` adds `activeClass` while `Router.IsActive(href)` is true
  (prefix match; `/` only matches exactly).
- In templates: `<Link href="/x">…</Link>`, `<NavLink href="/x" activeClass="on">…</NavLink>`.
- From code: `Router.Current!.Navigate("/todos/3")`, or `Navigate(path, replace: true)` to replace
  the history entry.
- `Router.Current!.Path` is a signal with the full path including query and hash; `PathName` is
  the path without them. The back button works as usual.

## Hosting

`BrowserApp.Start` installs the browser router. The server must return `index.html` for every
extension-less path (SPA fallback), and `index.html`/`app.mjs` must use absolute asset URLs
(`/browser/...`). See [Publishing](deployment.md#hosting).

There is no base-path or hash routing yet, so the app must be served from the site root.

## Testing

Tests use `MemoryHistory` instead of the browser history:

```csharp
Router.Install(new MemoryHistory("/todos/3"));
```
