using static Microsoft.Playwright.Assertions;

namespace NetWasm.UI.E2E;

/// <summary>NwUi.Workers: results, progress, a responsive page, cooperative cancel, JSON, streams and a pool.</summary>
[Collection(AppCollection.Name)]
public sealed class WorkersTests : PageTest
{
    private static readonly Microsoft.Playwright.LocatorAssertionsToHaveTextOptions Slow = new() { Timeout = 60_000 };

    public WorkersTests(WorkersServerFixture app) : base(app)
    {
    }

    private async Task OpenWorkersAsync()
    {
        await Page.GotoAsync("/");
        await Page.Locator("body[data-ready=true]").WaitForAsync();
    }

    [Fact]
    public async Task The_worker_counts_primes_and_reports_progress()
    {
        await OpenWorkersAsync();
        await Page.ClickAsync("#small");
        await Expect(Page.Locator("#status")).ToHaveTextAsync("78498 primes up to 1000000.", Slow);
        Assert.Equal(100, await Page.Locator("#progress").EvaluateAsync<int>("p => p.value"));
    }

    [Fact]
    public async Task The_page_stays_responsive_while_the_worker_runs()
    {
        await OpenWorkersAsync();
        await Page.ClickAsync("#large");
        await Expect(Page.Locator("#status")).ToHaveTextAsync("Counting to 500000000…");
        await Page.ClickAsync("#clicks");
        await Page.ClickAsync("#clicks");
        await Expect(Page.Locator("#clicks")).ToHaveTextAsync("Clicked 2 times");
        await Page.ClickAsync("#cancel");
    }

    [Fact]
    public async Task Cancel_stops_the_worker_cooperatively_and_keeps_its_state()
    {
        await OpenWorkersAsync();
        await Page.ClickAsync("#large");
        await Expect(Page.Locator("#status")).ToHaveTextAsync("Counting to 500000000…");
        await Page.WaitForTimeoutAsync(500);
        await Page.ClickAsync("#cancel");
        await Expect(Page.Locator("#status")).ToHaveTextAsync("Idle.");

        await Page.ClickAsync("#small");
        await Expect(Page.Locator("#status")).ToHaveTextAsync("78498 primes up to 1000000.", Slow);
        await Page.ClickAsync("#runs");
        await Expect(Page.Locator("#result")).ToHaveTextAsync("2 counts started.");
    }

    [Fact]
    public async Task Json_arguments_and_results_cross_the_worker_boundary()
    {
        await OpenWorkersAsync();
        await Page.ClickAsync("#stats");
        await Expect(Page.Locator("#result")).ToHaveTextAsync("25 primes, largest 97, 8 twin pairs.", Slow);
    }

    [Fact]
    public async Task A_stream_can_be_left_early_and_the_worker_stays_usable()
    {
        await OpenWorkersAsync();
        await Page.ClickAsync("#stream");
        await Expect(Page.Locator("#result")).ToHaveTextAsync("1009, 1013, 1019, 1021, 1031, 1033, 1039, 1049, 1051, 1061", Slow);
        await Page.ClickAsync("#runs");
        await Expect(Page.Locator("#result")).ToHaveTextAsync("0 counts started.", Slow);
    }

    [Fact]
    public async Task A_pool_runs_parts_in_parallel()
    {
        await OpenWorkersAsync();
        await Page.ClickAsync("#parallel");
        await Expect(Page.Locator("#pool")).ToHaveTextAsync("283146 primes up to 4000000.", Slow);
    }
}