using static Microsoft.Playwright.Assertions;

namespace NetWasm.UI.E2E;

/// <summary>
/// samples/Counter is published with NetWasmUIPrerender: /, /counter and /todos are in their HTML before the
/// Wasm loads, and the app replaces that markup when it starts.
/// </summary>
public sealed class PrerenderTests : PageTest
{
    public PrerenderTests(AppServerFixture app) : base(app)
    {
    }

    [Fact]
    public async Task Pages_have_their_content_without_javascript()
    {
        await using var context = await App.Browser.NewContextAsync(new() { BaseURL = App.BaseUrl, JavaScriptEnabled = false });
        var page = await context.NewPageAsync();

        await page.GotoAsync("/todos");
        await Expect(page.Locator("#app[data-nwui-prerender] li.todo")).ToHaveCountAsync(3);
        await Expect(page.Locator("nav a.active")).ToHaveTextAsync("Todos");

        await page.GotoAsync("/counter");
        await Expect(page.Locator("#count")).ToHaveTextAsync("0");
        await Expect(page.Locator("#reset")).ToBeDisabledAsync();

        // A path that is not prerendered gets the bare page.
        await page.GotoAsync("/greet/world");
        await Expect(page.Locator("#app")).ToBeEmptyAsync();
    }

    [Fact]
    public async Task The_app_replaces_the_prerendered_markup()
    {
        await OpenAsync("/todos");
        await Expect(Page.Locator("main")).ToHaveCountAsync(1);
        await Expect(Page.Locator("li.todo")).ToHaveCountAsync(3);
        Assert.DoesNotContain("nwui:prerender", await Page.InnerHTMLAsync("#app"));

        await Page.FillAsync("#new-todo", "Prerendered");
        await Page.ClickAsync("#todos button[type=submit]");
        await Expect(Page.Locator("li.todo")).ToHaveCountAsync(4);
    }
}
