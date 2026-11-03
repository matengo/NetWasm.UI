# Security

## What NetWasm.UI does for you

- **Text is text.** Everything that `Text(...)` and `@expression` render, attribute values and
  form values is set with `textContent`, `setAttribute` or the property, never as HTML. User
  input cannot inject markup. The E2E tests (`SecurityTests`) check this with route parameters
  and typed input.
- **No `eval`.** The renderer sends a binary batch of DOM operations to `dom.mjs`. There is no
  generated JavaScript at runtime and no `new Function`.
- **Templates are compile-time HTML.** The static HTML of a `.nwui` template is parsed once with
  `innerHTML` from a string the source generator produced. It never contains runtime data.

## What you must do yourself

- **Treat `Template(html)` like code.** If you call the low-level `Template` API from C#, the
  HTML is trusted. Never build it from user data.
- **Check URLs from users.** `href` and `src` are set as attributes, so a user-supplied
  `javascript:` URL is still a script when clicked. Allow only `http:`, `https:` or relative
  URLs:

  ```csharp
  static string SafeUrl(string url) =>
      url.StartsWith("https://", StringComparison.Ordinal) ||
      url.StartsWith("http://", StringComparison.Ordinal) ||
      url.StartsWith("/", StringComparison.Ordinal) ? url : "#";
  ```

- **Raw HTML.** There is no `innerHTML` binding. If you add one through your own `[JSImport]`,
  sanitize the HTML first (for example with DOMPurify).
- **Secrets.** Everything in the `.wasm` file, including strings and constants, can be read by
  anyone who downloads it. Keep API keys on the server.

## Content Security Policy

This is a strict policy that works. It is the one `SecurityTests` runs against `samples/Counter`
in the CI browsers:

```
default-src 'self';
script-src 'self' 'wasm-unsafe-eval' blob:;
style-src 'self' 'unsafe-inline';
object-src 'none';
base-uri 'self';
frame-ancestors 'none';
require-trusted-types-for 'script';
trusted-types nwui
```

| Directive | Why |
|---|---|
| `'wasm-unsafe-eval'` | Lets the page compile WebAssembly. It does not allow JavaScript `eval`. |
| `blob:` in `script-src` | The NetWasm runtime loads part of itself as a module from a `blob:` URL. |
| `trusted-types nwui` | `dom.mjs` creates a Trusted Types policy named `nwui` for template HTML. |
| `require-trusted-types-for 'script'` | Optional. It makes the browser reject any other `innerHTML`. |
| `style-src 'unsafe-inline'` | `Style(...)` and `style="@…"` set inline `style` attributes. Without dynamic styles you can use `style-src 'self'`, or relax only `style-src-attr`. |
| `connect-src` | Covered by `default-src 'self'`. Add your API origins if `HttpClient` calls other hosts. |

Send the policy as an HTTP header (`Content-Security-Policy`) from your host. A `<meta>` tag
works too, but it cannot set `frame-ancestors`.

The dev server (`nwui-dev`) injects a small live-reload script and opens a WebSocket. Test
your CSP against a published build, or run the dev server with `--no-watch`.

## Reporting a vulnerability

Please report security issues privately through
[GitHub security advisories](https://github.com/matengo/NetWasm.UI/security/advisories/new),
not in public issues.
