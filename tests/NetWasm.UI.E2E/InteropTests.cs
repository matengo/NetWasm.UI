using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace NetWasm.UI.E2E;

/// <summary>A JS library wrapped in a component: [JSImport], JSObject, JSSubscription and a Promise import.</summary>
[Collection(AppCollection.Name)]
public sealed class InteropTests : PageTest
{
    public InteropTests(InteropServerFixture app) : base(app)
    {
    }

    private async Task OpenInteropAsync()
    {
        await Page.GotoAsync("/");
        await Page.Locator("body[data-ready=true]").WaitForAsync();
    }

    private Task<int> LiveMetersAsync() =>
        Page.EvaluateAsync<int>("async () => (await import('/vendor/meter-lib.mjs')).liveMeters()");

    [Fact]
    public async Task The_library_renders_into_the_component()
    {
        await OpenInteropAsync();
        await Expect(Page.Locator(".meter .meter-label")).ToHaveTextAsync("3 / 10");
        Assert.Equal(1, await LiveMetersAsync());
    }

    [Fact]
    public async Task Signal_changes_reach_the_library()
    {
        await OpenInteropAsync();
        await Page.ClickAsync("#up");
        await Page.ClickAsync("#up");
        await Expect(Page.Locator(".meter .meter-label")).ToHaveTextAsync("5 / 10");
        await Expect(Page.Locator("#level")).ToHaveTextAsync("5");
    }

    [Fact]
    public async Task Library_callbacks_write_the_signal()
    {
        await OpenInteropAsync();
        var meter = Page.Locator(".meter");
        var box = (await meter.BoundingBoxAsync())!;
        await meter.ClickAsync(new() { Position = new() { X = box.Width * 0.8f, Y = box.Height / 2 } });
        await Expect(Page.Locator("#level")).ToHaveTextAsync("8");
        await Expect(Page.Locator(".meter .meter-label")).ToHaveTextAsync("8 / 10");
    }

    [Fact]
    public async Task Cleanup_destroys_the_library_instance()
    {
        await OpenInteropAsync();
        await Page.ClickAsync("#toggle");
        await Expect(Page.Locator(".meter")).ToHaveCountAsync(0);
        Assert.Equal(0, await LiveMetersAsync());

        await Page.ClickAsync("#toggle");
        await Expect(Page.Locator(".meter .meter-label")).ToHaveTextAsync("3 / 10");
        Assert.Equal(1, await LiveMetersAsync());
    }
}
