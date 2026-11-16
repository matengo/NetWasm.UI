using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace NetWasm.UI.E2E;

/// <summary>Element calls, document listeners, DOM reads, portals and generic components in a real browser.</summary>
[Collection(AppCollection.Name)]
public sealed class PrimitivesTests : PageTest
{
    public PrimitivesTests(PrimitivesServerFixture app) : base(app)
    {
    }

    private async Task OpenPrimitivesAsync()
    {
        await Page.GotoAsync("/");
        await Page.Locator("#app main h1").WaitForAsync();
    }

    [Fact]
    public async Task Dialog_opens_modally_and_closes_on_escape()
    {
        await OpenPrimitivesAsync();
        var dialog = Page.Locator("dialog#demo-dialog");
        await Expect(dialog).Not.ToBeVisibleAsync();

        await Page.ClickAsync("#open-dialog");
        await Expect(dialog).ToBeVisibleAsync();
        Assert.True(await dialog.EvaluateAsync<bool>("d => d.open && d.matches(':modal')"));
        await Expect(Page.Locator("#dialog-state")).ToHaveTextAsync("open");

        await Page.Keyboard.PressAsync("Escape");
        await Expect(dialog).Not.ToBeVisibleAsync();
        await Expect(Page.Locator("#dialog-state")).ToHaveTextAsync("closed");

        await Page.ClickAsync("#open-dialog");
        await dialog.Locator(".dialog-close").ClickAsync();
        await Expect(dialog).Not.ToBeVisibleAsync();
        await Expect(Page.Locator("#dialog-state")).ToHaveTextAsync("closed");
    }

    [Fact]
    public async Task Popover_renders_in_a_portal_and_closes_on_outside_click_and_escape()
    {
        await OpenPrimitivesAsync();
        var trigger = Page.Locator("#popover-trigger");
        var panel = Page.Locator(".popover");
        await Expect(panel).ToBeHiddenAsync();

        await trigger.ClickAsync();
        await Expect(panel).ToBeVisibleAsync();
        await Expect(trigger).ToHaveAttributeAsync("aria-expanded", "true");
        Assert.True(await panel.EvaluateAsync<bool>("p => p.parentElement.matches('body > [data-nwui-portal]')"));
        var box = await trigger.BoundingBoxAsync();
        var panelBox = await panel.BoundingBoxAsync();
        Assert.True(panelBox!.Y >= box!.Y + box.Height, "the panel is positioned below the trigger");

        await Page.ClickAsync("#popover-inner");
        await Expect(panel).ToBeVisibleAsync();

        await Page.ClickAsync("h1");
        await Expect(panel).ToBeHiddenAsync();
        await Expect(Page.Locator("#popover-state")).ToHaveTextAsync("closed");

        await trigger.ClickAsync();
        await Expect(panel).ToBeVisibleAsync();
        await trigger.ClickAsync();
        await Expect(panel).ToBeHiddenAsync();

        await trigger.ClickAsync();
        await Page.ClickAsync("#popover-inner");
        await Page.Keyboard.PressAsync("Escape");
        await Expect(panel).ToBeHiddenAsync();
        await Expect(trigger).ToBeFocusedAsync();
    }

    [Fact]
    public async Task Tabs_move_selection_and_focus_with_the_keyboard()
    {
        await OpenPrimitivesAsync();
        var tabs = Page.Locator("[role=tab]");
        await tabs.Nth(0).ClickAsync();
        await Page.Keyboard.PressAsync("ArrowRight");
        await Expect(tabs.Nth(1)).ToBeFocusedAsync();
        await Expect(tabs.Nth(1)).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(tabs.Nth(1)).ToHaveAttributeAsync("tabindex", "0");
        await Expect(tabs.Nth(0)).ToHaveAttributeAsync("tabindex", "-1");
        await Expect(Page.Locator("[role=tabpanel]:visible")).ToHaveTextAsync("Panel 2");

        await Page.Keyboard.PressAsync("ArrowLeft");
        await Page.Keyboard.PressAsync("ArrowLeft");
        await Expect(tabs.Nth(2)).ToBeFocusedAsync();
        await Expect(Page.Locator("[role=tabpanel]:visible")).ToHaveTextAsync("Panel 3");
    }

    [Fact]
    public async Task Generic_components_share_a_bound_selection()
    {
        await OpenPrimitivesAsync();
        await Expect(Page.Locator("#select-state")).ToHaveTextAsync("Selected: none");

        await Page.ClickAsync(".select-trigger");
        await Page.Locator(".select [role=option]", new() { HasText = "Banana" }).ClickAsync();
        await Expect(Page.Locator(".select [role=listbox]")).ToHaveCountAsync(0);
        await Expect(Page.Locator(".select-trigger")).ToContainTextAsync("Banana");
        await Expect(Page.Locator("#select-state")).ToHaveTextAsync("Selected: Banana");
        await Expect(Page.Locator("tbody tr[aria-selected=true]")).ToContainTextAsync("Banana");

        await Page.Locator("tbody tr", new() { HasText = "Cherry" }).ClickAsync();
        await Expect(Page.Locator("#select-state")).ToHaveTextAsync("Selected: Cherry");
        await Expect(Page.Locator(".select-trigger")).ToContainTextAsync("Cherry");
    }
}
