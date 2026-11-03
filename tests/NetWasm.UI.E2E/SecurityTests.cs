using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace NetWasm.UI.E2E;

/// <summary>
/// The app under a strict Content Security Policy with Trusted Types (see docs/security.md), and
/// user data that looks like markup: it must always end up as text.
/// </summary>
public sealed class SecurityTests : PageTest
{
    /// <summary>The policy docs/security.md recommends; keep the two in sync.</summary>
    public const string StrictCsp =
        "default-src 'self'; script-src 'self' 'wasm-unsafe-eval' blob:; style-src 'self' 'unsafe-inline'; " +
        "object-src 'none'; base-uri 'self'; frame-ancestors 'none'; " +
        "require-trusted-types-for 'script'; trusted-types nwui";

    public SecurityTests(AppServerFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task Runs_under_a_strict_CSP_with_Trusted_Types()
    {
        await Page.RouteAsync("**/*", async route =>
        {
            if (route.Request.ResourceType != "document")
            {
                await route.ContinueAsync();
                return;
            }

            var response = await route.FetchAsync();
            var headers = new Dictionary<string, string>(response.Headers) { ["content-security-policy"] = StrictCsp };
            await route.FulfillAsync(new() { Response = response, Headers = headers });
        });
        await Page.AddInitScriptAsync(
            "window.__violations = [];" +
            "document.addEventListener('securitypolicyviolation', e => window.__violations.push(e.violatedDirective + ' ' + e.blockedURI));");

        // Templates (innerHTML through the "nwui" policy), events, reactive style attributes and routing.
        await OpenAsync("/components");
        await Expect(Page.Locator("#components h2")).ToBeVisibleAsync();
        await OpenAsync("/counter");
        await Page.ClickAsync("#inc");
        await Expect(Page.Locator("#count")).ToHaveTextAsync("1");
        await Nav("/todos").ClickAsync();
        await Expect(Page.Locator("#todos")).ToBeVisibleAsync();

        var violations = await Page.EvaluateAsync<string[]>("window.__violations");
        Assert.Empty(violations);
    }

    [Fact]
    public async Task Markup_in_route_parameters_and_query_is_rendered_as_text()
    {
        // No "." in the path segment: dev servers treat such paths as files, not app routes.
        const string payload = "<img src=x onerror=\"__xss=1\">";
        await OpenAsync("/greet/" + Uri.EscapeDataString(payload) + "?q=" + Uri.EscapeDataString("<script>window.__xss=2</script>"));

        await Expect(Page.Locator("h2")).ToHaveTextAsync("Hello, " + payload + "!");
        Assert.Equal(0, await Page.Locator("#app img, #app script").CountAsync());
        Assert.Null(await Page.EvaluateAsync<int?>("window.__xss ?? null"));
    }

    [Fact]
    public async Task Markup_typed_into_inputs_is_rendered_as_text()
    {
        const string payload = "\"><img src=x onerror=\"window.__xss=3\">";
        await OpenAsync("/greet/world");
        await Page.FillAsync("#greet-name", payload);
        await Page.ClickAsync("#greet-go");

        await Expect(Page.Locator("h2")).ToHaveTextAsync("Hello, " + payload + "!");
        Assert.Equal(0, await Page.Locator("#app img").CountAsync());
        Assert.Null(await Page.EvaluateAsync<int?>("window.__xss ?? null"));
    }
}
