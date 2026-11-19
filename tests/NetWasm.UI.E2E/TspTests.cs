using static Microsoft.Playwright.Assertions;

namespace NetWasm.UI.E2E;

/// <summary>samples/Tsp: a pool of workers streams better tours to the page, which stays responsive and can stop them.</summary>
[Collection(AppCollection.Name)]
public sealed class TspTests : PageTest
{
    private static readonly Microsoft.Playwright.LocatorAssertionsToHaveTextOptions Slow = new() { Timeout = 60_000 };

    public TspTests(TspServerFixture app) : base(app)
    {
    }

    private async Task OpenTspAsync()
    {
        await Page.GotoAsync("/");
        await Page.Locator("body[data-ready=true]").WaitForAsync();
    }

    [Fact]
    public async Task The_workers_find_a_shorter_tour_through_every_point()
    {
        await OpenTspAsync();
        await Page.SelectOptionAsync("#count", "200");
        await Expect(Page.Locator("#status")).ToHaveTextAsync("200 points. Press Solve.");
        await Page.SelectOptionAsync("#time", "5");
        await Page.ClickAsync("#solve");
        await Expect(Page.Locator("#status")).ToHaveTextAsync("Done.", Slow);

        var stats = Page.Locator("#stats");
        var first = long.Parse(await stats.GetAttributeAsync("data-first") ?? "0");
        var best = long.Parse(await stats.GetAttributeAsync("data-best") ?? "0");
        Assert.InRange(best, 1, first - 1);

        var workers = int.Parse(await Page.Locator("#workers").InputValueAsync());
        await Expect(Page.Locator(".runs tbody tr")).ToHaveCountAsync(workers);
        await Expect(Page.Locator(".runs tbody tr").First.Locator("td").Nth(1)).ToHaveTextAsync("done");

        // A closed tour: every point once, back to the start.
        var points = await Page.Locator("polyline.tour").EvaluateAsync<int>("p => p.points.numberOfItems");
        Assert.Equal(201, points);
    }

    [Fact]
    public async Task The_page_stays_responsive_and_stop_ends_an_open_ended_run()
    {
        await OpenTspAsync();
        await Page.SelectOptionAsync("#count", "2000");
        await Page.SelectOptionAsync("#time", "0");
        await Page.ClickAsync("#solve");
        await Expect(Page.Locator("#status")).ToContainTextAsync("Solving");
        await Expect(Page.Locator("#stats")).Not.ToHaveAttributeAsync("data-best", "0");

        await Page.ClickAsync("#clicks");
        await Page.ClickAsync("#clicks");
        await Expect(Page.Locator("#clicks")).ToHaveTextAsync("Clicked 2 times");

        await Page.ClickAsync("#stop");
        await Expect(Page.Locator("#status")).ToHaveTextAsync("Stopped.", Slow);
        await Expect(Page.Locator("#solve")).ToBeEnabledAsync();
    }
}
