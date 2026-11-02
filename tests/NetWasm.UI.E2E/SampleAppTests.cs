using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace NetWasm.UI.E2E;

public sealed class CounterTests : PageTest
{
    public CounterTests(AppServerFixture app) : base(app)
    {
    }

    [Fact]
    public async Task Buttons_update_count_and_derived_state()
    {
        await OpenAsync("/counter");
        var count = Page.Locator("#count");
        await Expect(count).ToHaveTextAsync("0");
        await Expect(Page.Locator("#reset")).ToBeDisabledAsync();

        await Page.ClickAsync("#inc");
        await Page.ClickAsync("#inc");
        await Page.ClickAsync("#dec");
        await Expect(count).ToHaveTextAsync("1");
        await Expect(Page.Locator("#counter p").First).ToContainTextAsync("doubled: 2");
        await Expect(Page.Locator("#reset")).ToBeEnabledAsync();

        await Page.ClickAsync("#reset");
        await Expect(count).ToHaveTextAsync("0");
    }

    [Fact]
    public async Task Hint_appears_at_ten_clicks()
    {
        await OpenAsync("/counter");
        var hint = Page.Locator(".hint");
        for (var i = 0; i < 9; i++)
        {
            await Page.ClickAsync("#inc");
        }

        await Expect(Page.Locator("#count")).ToHaveTextAsync("9");
        await Expect(hint).ToHaveCountAsync(0);
        await Page.ClickAsync("#inc");
        await Expect(hint).ToBeVisibleAsync();
    }
}

public sealed class TodoTests : PageTest
{
    public TodoTests(AppServerFixture app) : base(app)
    {
    }

    [Fact]
    public async Task Add_toggle_remove_and_clear()
    {
        await OpenAsync("/todos");
        var items = Page.Locator("li.todo");
        var status = Page.Locator("#todos .status");
        await Expect(items).ToHaveCountAsync(3);
        await Expect(status).ToHaveTextAsync("3 of 3 remaining");

        var input = Page.Locator("#new-todo");
        await Expect(input).ToBeFocusedAsync();          // OnMount(Focus)
        var add = Page.Locator("#todos button[type=submit]");
        await Expect(add).ToBeDisabledAsync();
        await input.FillAsync("Write E2E tests");
        await input.PressAsync("Enter");
        await Expect(items).ToHaveCountAsync(4);
        await Expect(items.Last).ToContainTextAsync("Write E2E tests");
        await Expect(input).ToHaveValueAsync("");

        await items.Nth(0).Locator("input[type=checkbox]").CheckAsync();
        await Expect(items.Nth(0)).ToHaveClassAsync("todo done");
        await Expect(status).ToHaveTextAsync("3 of 4 remaining");

        await items.Nth(1).Locator(".remove").ClickAsync();
        await Expect(items).ToHaveCountAsync(3);

        await Page.ClickAsync("#clear-done");
        await Expect(items).ToHaveCountAsync(2);
        await Expect(status).ToHaveTextAsync("2 of 2 remaining");
    }
}

public sealed class RoutingTests : PageTest
{
    public RoutingTests(AppServerFixture app) : base(app)
    {
    }

    [Fact]
    public async Task Nav_links_route_client_side_and_mark_active()
    {
        await OpenAsync("/");
        await Expect(Nav("/")).ToHaveClassAsync("active");

        // A marker on window survives client-side navigation but not a full page load.
        await Page.EvaluateAsync("() => window.__marker = 42");
        await Nav("/counter").ClickAsync();
        await Expect(Page).ToHaveURLAsync(App.BaseUrl + "/counter");
        await Expect(Page.Locator("#counter")).ToBeVisibleAsync();
        await Expect(Nav("/counter")).ToHaveClassAsync("active");
        await Expect(Nav("/")).Not.ToHaveClassAsync("active");
        Assert.Equal(42, await Page.EvaluateAsync<int>("() => window.__marker"));
    }

    [Fact]
    public async Task Back_and_forward_buttons_work()
    {
        await OpenAsync("/");
        await Nav("/counter").ClickAsync();
        await Expect(Page.Locator("#counter")).ToBeVisibleAsync();
        await Nav("/todos").ClickAsync();
        await Expect(Page.Locator("#todos")).ToBeVisibleAsync();

        await Page.GoBackAsync();
        await Expect(Page.Locator("#counter")).ToBeVisibleAsync();
        await Page.GoBackAsync();
        await Expect(Page.Locator(".tagline")).ToBeVisibleAsync();
        await Page.GoForwardAsync();
        await Expect(Page.Locator("#counter")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Deep_link_with_encoded_param_and_query()
    {
        await OpenAsync("/greet/Ada%20Lovelace?x=1");
        await Expect(Page.Locator("h2")).ToHaveTextAsync("Hello, Ada Lovelace!");
        await Expect(Page.Locator("section .status")).ToHaveTextAsync("Query: 'x=1'");
    }

    [Fact]
    public async Task Programmatic_navigation_escapes_parameters()
    {
        await OpenAsync("/greet/world");
        await Page.FillAsync("#greet-name", "Åsa & Ö");
        await Page.ClickAsync("#greet-go");
        await Expect(Page.Locator("h2")).ToHaveTextAsync("Hello, Åsa & Ö!");
        Assert.Equal(App.BaseUrl + "/greet/%C3%85sa%20%26%20%C3%96", Page.Url);
    }

    [Fact]
    public async Task Unknown_path_shows_not_found()
    {
        await OpenAsync("/no/such/page");
        await Expect(Page.Locator("h2")).ToHaveTextAsync("404");
        await Expect(Page.Locator("section code")).ToHaveTextAsync("/no/such/page");
    }

    [Fact]
    public async Task Ctrl_click_is_not_intercepted()
    {
        await OpenAsync("/");
        var popup = Page.Context.WaitForPageAsync();
        await Nav("/todos").ClickAsync(new() { Modifiers = new[] { KeyboardModifier.ControlOrMeta } });
        var tab = await popup;
        await tab.WaitForURLAsync("**/todos");
        await Expect(Page).ToHaveURLAsync(App.BaseUrl + "/");     // original tab stayed
        await tab.CloseAsync();
    }
}

public sealed class StoreTests : PageTest
{
    public StoreTests(AppServerFixture app) : base(app)
    {
    }

    [Fact]
    public async Task App_state_survives_navigation()
    {
        await OpenAsync("/counter");
        for (var i = 0; i < 3; i++)
        {
            await Page.ClickAsync("#inc");
        }

        await Expect(Page.Locator("#count")).ToHaveTextAsync("3");
        await Nav("/todos").ClickAsync();
        await Page.FillAsync("#new-todo", "Kept");
        await Page.PressAsync("#new-todo", "Enter");
        await Page.FillAsync("#new-todo", "draft text");
        await Expect(Page.Locator("li.todo")).ToHaveCountAsync(4);

        await Nav("/counter").ClickAsync();
        await Expect(Page.Locator("#count")).ToHaveTextAsync("3");
        await Nav("/todos").ClickAsync();
        await Expect(Page.Locator("li.todo")).ToHaveCountAsync(4);
        await Expect(Page.Locator("#new-todo")).ToHaveValueAsync("");   // draft is page-local
    }
}

public sealed class ComponentsTests : PageTest
{
    public ComponentsTests(AppServerFixture app) : base(app)
    {
    }

    [Theory]
    [InlineData(0, "Razor (.nwui)")]
    [InlineData(1, "C# (code)")]
    public async Task Nested_components_behave_the_same_in_both_variants(int index, string title)
    {
        await OpenAsync("/components");
        var panel = Page.Locator(".panel").Nth(index);
        var steppers = panel.Locator(".stepper");
        var width = steppers.Nth(0);
        var length = steppers.Nth(1);
        var doors = steppers.Nth(2);
        await Expect(panel.Locator("h3")).ToHaveTextAsync(title);
        await Expect(steppers).ToHaveCountAsync(4);
        await Expect(width.Locator(".dec")).ToHaveTextAsync("\u2212");     // &minus;
        await Expect(panel.Locator(".progress-text")).ToHaveTextAsync("12 / 200");
        await Expect(panel.Locator(".summary")).ToHaveTextAsync("12 m² with 3 extras");

        await width.Locator(".inc").ClickAsync();
        await length.Locator(".inc").ClickAsync();      // Step = 2
        await Expect(width.Locator("output")).ToHaveTextAsync("4");
        await Expect(length.Locator("output")).ToHaveTextAsync("6");
        await Expect(panel.Locator(".progress-text")).ToHaveTextAsync("24 / 200");
        await Expect(panel.Locator(".progress-fill")).ToHaveAttributeAsync("style", "width: 12%");

        await doors.Locator(".inc").ClickAsync();
        await doors.Locator(".inc").ClickAsync();
        await Expect(doors.Locator("output")).ToHaveTextAsync("3");
        await Expect(doors.Locator(".inc")).ToBeDisabledAsync();   // Max = 3
        await Expect(panel.Locator(".summary")).ToHaveTextAsync("24 m² with 5 extras");

        // The other variant has its own state.
        await Expect(Page.Locator(".panel").Nth(1 - index).Locator(".summary")).ToHaveTextAsync("12 m² with 3 extras");

        await panel.Locator(".reset").ClickAsync();         // Actions slot
        await Expect(panel.Locator(".summary")).ToHaveTextAsync("12 m² with 3 extras");
        await Expect(width.Locator(".dec")).ToBeEnabledAsync();
    }

    [Fact]
    public async Task Error_boundary_shows_fallback_and_retries()
    {
        await OpenAsync("/components");
        var fragile = Page.Locator("#fragile");
        await fragile.ClickAsync();
        await fragile.ClickAsync();
        await Expect(fragile).ToHaveTextAsync("Clicks: 2 (fails on 3)");

        await fragile.ClickAsync();
        await Expect(Page.Locator(".guarded .error")).ToContainTextAsync("Oops: click number 3 failed");
        await Expect(fragile).ToHaveCountAsync(0);

        await Page.ClickAsync("#retry");
        await Expect(fragile).ToHaveTextAsync("Clicks: 3 (fails on 3)");
        await fragile.ClickAsync();
        await Expect(fragile).ToHaveTextAsync("Clicks: 4 (fails on 3)");
    }
}
