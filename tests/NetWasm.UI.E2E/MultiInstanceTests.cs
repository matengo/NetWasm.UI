using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace NetWasm.UI.E2E;

/// <summary>Two instances of the same module, each with its own <c>createDomHost({ root })</c>.</summary>
[Collection(AppCollection.Name)]
public sealed class MultiInstanceTests : PageTest
{
    public MultiInstanceTests(WidgetsServerFixture app) : base(app)
    {
    }

    private async Task OpenWidgetsAsync()
    {
        await Page.GotoAsync("/");
        await Page.Locator("body[data-ready=true]").WaitForAsync();
    }

    [Fact]
    public async Task Instances_render_and_dispatch_independently()
    {
        await OpenWidgetsAsync();
        var first = Page.Locator("#first");
        var second = Page.Locator("#second");
        await Expect(first.Locator(".label")).ToHaveTextAsync("First");
        await Expect(second.Locator(".label")).ToHaveTextAsync("Second");

        await first.Locator(".inc").ClickAsync();
        await first.Locator(".inc").ClickAsync();
        await second.Locator(".inc").ClickAsync();

        await Expect(first.Locator(".count")).ToHaveTextAsync("2");
        await Expect(second.Locator(".count")).ToHaveTextAsync("1");
    }

    [Fact]
    public async Task Closing_one_instance_leaves_the_other_working()
    {
        await OpenWidgetsAsync();
        await Page.ClickAsync("#close-second");
        await Expect(Page.Locator("#second .widget")).ToHaveCountAsync(0);

        var first = Page.Locator("#first");
        await first.Locator(".inc").ClickAsync();
        await Expect(first.Locator(".count")).ToHaveTextAsync("1");
    }
}
