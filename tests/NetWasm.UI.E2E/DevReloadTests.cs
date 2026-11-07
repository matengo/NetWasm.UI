using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace NetWasm.UI.E2E;

/// <summary>
/// The dev server's reload client keeps signal values, form fields and focus across a reload it starts.
/// The test server runs with --no-watch (no client injected), so the client is added as an init script.
/// </summary>
public sealed class DevReloadTests : PageTest
{
    public DevReloadTests(AppServerFixture app) : base(app)
    {
    }

    private async Task UseReloadClientAsync()
    {
        using var http = new HttpClient();
        var script = await http.GetStringAsync(App.BaseUrl + "/__netwasm_ui/reload-client.js");
        await Page.AddInitScriptAsync(script);
    }

    private async Task DevReloadAsync()
    {
        var loaded = new TaskCompletionSource();
        void OnLoad(object? sender, IPage page) => loaded.TrySetResult();
        Page.Load += OnLoad;
        try
        {
            await Page.EvaluateAsync("() => { globalThis.__nwuiDev.reload(); }");
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            Page.Load -= OnLoad;
        }
    }

    [Fact]
    public async Task Signals_survive_a_dev_reload_but_not_a_manual_one()
    {
        await UseReloadClientAsync();
        await OpenAsync("/counter");
        var count = Page.Locator("#count");
        for (var i = 0; i < 3; i++)
        {
            await Page.ClickAsync("#inc");
        }

        await Expect(count).ToHaveTextAsync("3");

        await DevReloadAsync();
        await Expect(count).ToHaveTextAsync("3");
        await Expect(Page.Locator("#counter p").First).ToContainTextAsync("doubled: 6");

        await Page.ReloadAsync();
        await Expect(count).ToHaveTextAsync("0");
    }

    [Fact]
    public async Task Fields_checkboxes_and_focus_survive_a_dev_reload()
    {
        await UseReloadClientAsync();
        await OpenAsync("/todos");
        var input = Page.Locator("#new-todo");
        var first = Page.Locator("li.todo").First;
        await first.Locator("input[type=checkbox]").CheckAsync();
        await input.FillAsync("half typed");
        await Expect(input).ToBeFocusedAsync();

        await DevReloadAsync();
        await Expect(input).ToHaveValueAsync("half typed");
        await Expect(input).ToBeFocusedAsync();
        await Expect(first).ToHaveClassAsync("todo done");
        await Expect(Page.Locator("#todos button[type=submit]")).ToBeEnabledAsync();
    }
}
