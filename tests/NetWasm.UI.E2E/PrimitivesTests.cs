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
    public async Task Rows_and_blocks_inside_svg_are_drawn_as_svg_elements()
    {
        await OpenPrimitivesAsync();
        const string Namespaces = "sel => [...document.querySelectorAll(sel)].map(e => e.namespaceURI)";
        const string Svg = "http://www.w3.org/2000/svg";

        Assert.Equal([Svg, Svg, Svg], await Page.EvaluateAsync<string[]>(Namespaces, "#chart rect.bar"));
        Assert.True(await Page.Locator("#chart rect.bar").First.EvaluateAsync<bool>("e => e.getBBox().height > 0"));
        Assert.Equal(["http://www.w3.org/1999/xhtml"], await Page.EvaluateAsync<string[]>(Namespaces, "#chart-note"));

        await Page.ClickAsync("#add-bar");
        await Page.ClickAsync("#toggle-average");
        await Expect(Page.Locator("#chart rect.bar")).ToHaveCountAsync(4);
        await Expect(Page.Locator("#chart-note")).ToHaveTextAsync("4 bars");
        Assert.Equal([Svg, Svg, Svg, Svg], await Page.EvaluateAsync<string[]>(Namespaces, "#chart rect.bar"));
        Assert.Equal([Svg], await Page.EvaluateAsync<string[]>(Namespaces, "#average"));
        Assert.True(await Page.Locator("#average").EvaluateAsync<bool>("e => e.getBBox().width > 0"));

        // A row's handler and its bound class work on SVG elements.
        await Page.Locator("#chart rect.bar").Nth(2).DispatchEventAsync("click");
        await Expect(Page.Locator("#selected-bar")).ToHaveTextAsync("bar 3");
        await Expect(Page.Locator("#chart rect.selected")).ToHaveCountAsync(1);
        Assert.Equal("bar selected", await Page.Locator("#chart rect.bar").Nth(2).GetAttributeAsync("class"));
    }

    [Fact]
    public async Task Library_stylesheet_asset_is_linked_before_the_app_styles_and_applied()
    {
        await OpenPrimitivesAsync();
        var order = await Page.EvaluateAsync<string[]>(
            "() => [...document.head.querySelectorAll('link[rel=stylesheet], style')].map(e => e.tagName === 'LINK' ? e.getAttribute('href') : 'style')");
        Assert.Equal(["/_content/Primitives.Theme/theme.css", "style"], order);

        await Page.ClickAsync("#popover-trigger");
        var shadow = await Page.Locator(".popover").EvaluateAsync<string>("e => getComputedStyle(e).boxShadow");
        Assert.NotEqual("none", shadow);
    }

    [Fact]
    public async Task Capture_listener_runs_before_a_target_that_stops_propagation()
    {
        await OpenPrimitivesAsync();
        await Page.ClickAsync("#capture-inner");
        await Expect(Page.Locator("#capture-log")).ToHaveTextAsync("outer inner");
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
    public async Task Headless_disclosure_and_themed_expander_share_behaviour()
    {
        await OpenPrimitivesAsync();
        var headless = Page.Locator("#headless-disclosure");
        var trigger = headless.Locator("button");
        await Expect(headless.Locator("[role=region]")).ToBeHiddenAsync();

        await trigger.ClickAsync();
        await Expect(trigger).ToHaveAttributeAsync("aria-expanded", "true");
        await Expect(headless).ToHaveAttributeAsync("data-state", "open");
        await Expect(headless.Locator("[role=region]")).ToBeVisibleAsync();
        await Expect(Page.Locator("#disclosure-log")).ToHaveTextAsync("opened");

        var expander = Page.Locator("#themed-expander");
        await Expect(expander).ToHaveClassAsync("ui-expander");
        Assert.NotEqual("0px", await expander.EvaluateAsync<string>("e => getComputedStyle(e).borderTopWidth"));
        await expander.Locator("button").PressAsync("Enter");
        await Expect(Page.Locator("#expander-state")).ToHaveTextAsync("open");
        await Expect(expander).ToHaveAttributeAsync("data-state", "open");
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

    [Fact]
    public async Task Focus_scope_moves_traps_and_restores_focus()
    {
        await OpenPrimitivesAsync();
        var trigger = Page.Locator("#open-panel");
        await trigger.ClickAsync();
        await Expect(Page.Locator("#panel-first")).ToBeFocusedAsync();

        // The disabled button is skipped and Tab wraps round in both directions.
        await Page.Keyboard.PressAsync("Tab");
        await Expect(Page.Locator("#panel-last")).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Tab");
        await Expect(Page.Locator("#panel-first")).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Shift+Tab");
        await Expect(Page.Locator("#panel-last")).ToBeFocusedAsync();

        await Page.Keyboard.PressAsync("Escape");
        await Expect(Page.Locator("#focus-panel")).ToHaveCountAsync(0);
        await Expect(trigger).ToBeFocusedAsync();

        await trigger.ClickAsync();
        await Page.ClickAsync("#panel-last");
        await Expect(Page.Locator("#focus-panel")).ToHaveCountAsync(0);
        await Expect(trigger).ToBeFocusedAsync();
    }

    [Fact]
    public async Task Animated_exit_keeps_the_node_until_its_animation_ends()
    {
        await OpenPrimitivesAsync();
        await Page.ClickAsync("#add-toast");
        await Page.ClickAsync("#add-toast");
        var toasts = Page.Locator("#toasts > li");
        await Expect(toasts).ToHaveCountAsync(2);
        await Expect(toasts.First).ToHaveAttributeAsync("data-state", "open");

        // The dismissed toast stays as closed (inert) while it fades, then leaves; the other is untouched.
        await toasts.First.Locator(".dismiss").ClickAsync();
        var closed = Page.Locator("#toasts > li[data-state=closed]");
        await Expect(closed).ToHaveCountAsync(1);
        Assert.True(await closed.EvaluateAsync<bool>("e => e.inert"));
        await Expect(closed).ToHaveCountAsync(0);
        await Expect(toasts).ToHaveCountAsync(1);
        await Expect(toasts.First).ToContainTextAsync("Toast 2");

        // Without a running animation (a style override turns it off) removal is immediate.
        await Page.AddStyleTagAsync(new() { Content = ".toast[data-state=closed] { animation: none !important; }" });
        await toasts.First.Locator(".dismiss").ClickAsync();
        await Expect(toasts).ToHaveCountAsync(0);
    }
}