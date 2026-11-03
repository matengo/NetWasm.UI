using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace NetWasm.UI.E2E;

/// <summary>HttpClient + System.Text.Json in the browser, driven by Resource and Suspense.</summary>
[Collection(AppCollection.Name)]
public sealed class FetchTests : PageTest
{
    public FetchTests(FetchServerFixture app) : base(app)
    {
    }

    private async Task OpenFetchAsync()
    {
        await Page.GotoAsync("/");
        await Page.Locator("body[data-ready=true]").WaitForAsync();
    }

    [Fact]
    public async Task Suspense_shows_the_list_once_loaded()
    {
        await OpenFetchAsync();
        await Expect(Page.Locator(".users li")).ToHaveCountAsync(3);
        await Expect(Page.Locator(".loading")).ToHaveCountAsync(0);
        await Expect(Page.Locator(".hint")).ToHaveTextAsync("Select a user.");
    }

    [Fact]
    public async Task Selecting_a_user_loads_details()
    {
        await OpenFetchAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Alan Turing" }).ClickAsync();
        await Expect(Page.Locator(".bio")).ToHaveTextAsync("Computable numbers");
    }

    [Fact]
    public async Task A_failed_request_shows_the_error()
    {
        AllowConsoleError("404");
        await OpenFetchAsync();
        await Page.ClickAsync("#missing");
        await Expect(Page.Locator(".bio")).ToContainTextAsync("Failed:");
        await Expect(Page.Locator(".bio")).ToContainTextAsync("404");
    }
}
