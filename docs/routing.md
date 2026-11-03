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

## Nested routes and layouts

A pattern that ends in `/*` is a prefix. The page it renders can contain its own `Routes`, and
those patterns are relative to the prefix. The outer page (the layout) stays mounted; only the
nested part changes.

```csharp
Routes(
    Route("/", Home),
    Route("/settings/*", () => SettingsLayout()));

private static Node SettingsLayout() =>
    Div(
        Nav(NavLink("/settings/profile", "active", "Profile"),
            NavLink("/settings/security", "active", "Security")),
        Routes(
            Route("/", () => P("Choose a section")),       // /settings
            Route("/profile", () => new ProfilePage().Render()),
            Route("/security", () => new SecurityPage().Render())));
```

Links and `Navigate` always take full app paths (`/settings/profile`), not paths relative to
the prefix.

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
- A navigation to a new page scrolls to the top, unless the path has a `#fragment`. Back and
  forward leave the scroll position to the browser.

## Hosting and base path

`BrowserApp.Start` installs the browser router. The server must return `index.html` for every
extension-less path (SPA fallback). See [Publishing](deployment.md#hosting).

To serve the app under a sub-path such as `https://example.com/my-app/`, choose one of these:

- Add `<base href="/my-app/">` to `index.html`. The router reads it.
- Pass the base path explicitly: `BrowserApp.Start(new App(), "#app", basePath: "/my-app/")`.
- Pass it as the `basePath` option of `createDomHost` (see [Embedding](embedding.md)).

Route patterns, `Router.Path`, `Link` and `Navigate` all use app paths without the prefix:
`Link("/todos")` renders `href="/my-app/todos"`. Use `Router.Current!.Href(path)` to build a URL
for anything else. Asset URLs in `index.html` and `app.mjs` must include the prefix, or be
relative to `<base href>`.

There is no hash routing.

## Testing

Tests use `MemoryHistory` instead of the browser history:

```csharp
Router.Install(new MemoryHistory("/todos/3"));
Router.Install(new MemoryHistory("/my-app/todos/3"), basePath: "/my-app");   // with a base path
```
