using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace NetWasm.UI.E2E;

/// <summary>The "/settings/*" layout route: nested Routes switch while the layout stays mounted.</summary>
public sealed class NestedRouteTests : PageTest
{
    public NestedRouteTests(AppServerFixture app) : base(app)
    {
    }

    [Fact]
    public async Task Layout_survives_child_navigation_and_back_button()
    {
        await OpenAsync("/settings");
        var layout = Page.Locator("#settings");
        await Expect(Page.Locator("#settings-page")).ToHaveTextAsync("Pick a section.");
        var build = await layout.GetAttributeAsync("data-build");

        await Page.Locator(".subnav").GetByText("Profile").ClickAsync();
        await Expect(Page.Locator("#settings-page")).ToHaveTextAsync("Profile settings.");
        await Expect(Page).ToHaveURLAsync(App.BaseUrl + "/settings/profile");
        await Expect(Page.Locator(".subnav a.active")).ToHaveTextAsync("Profile");

        await Page.Locator(".subnav").GetByText("Security").ClickAsync();
        await Expect(Page.Locator("#settings-page")).ToHaveTextAsync("Security settings.");

        await Page.GoBackAsync();
        await Expect(Page.Locator("#settings-page")).ToHaveTextAsync("Profile settings.");
        await Expect(layout).ToHaveAttributeAsync("data-build", build!);
    }

    [Fact]
    public async Task Deep_link_and_unknown_child()
    {
        await OpenAsync("/settings/nope");
        await Expect(Page.Locator("#settings-page")).ToHaveTextAsync("Unknown setting.");
        await Expect(Page.Locator("h2")).ToHaveTextAsync("Settings");
    }
}
