using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace NetWasm.UI.E2E;

/// <summary>The sign-up page: Field/Form validation, aria wiring, focus on invalid submit and async submit.</summary>
public sealed class FormTests : PageTest
{
    public FormTests(AppServerFixture app) : base(app)
    {
    }

    [Fact]
    public async Task Errors_appear_on_blur_with_aria()
    {
        await OpenAsync("/signup");
        var email = Page.Locator("#signup-email");
        await Expect(email).Not.ToHaveAttributeAsync("aria-invalid", "true");

        await email.FillAsync("nope");
        await email.BlurAsync();

        await Expect(email).ToHaveAttributeAsync("aria-invalid", "true");
        await Expect(email).ToHaveAttributeAsync("aria-describedby", "signup-email-error");
        await Expect(Page.Locator("#signup-email-error")).ToHaveTextAsync("Enter a valid e-mail address.");
        await Expect(Page.GetByLabel("E-mail")).ToHaveAccessibleDescriptionAsync("Enter a valid e-mail address.");

        await email.FillAsync("ada@example.com");
        await Expect(email).Not.ToHaveAttributeAsync("aria-invalid", "true");
        await Expect(Page.Locator("#signup-email-error")).ToHaveTextAsync("");
    }

    [Fact]
    public async Task Invalid_submit_shows_all_errors_and_focuses_the_first()
    {
        await OpenAsync("/signup");
        await Page.Locator("#signup-email").FillAsync("ada@example.com");
        await Page.ClickAsync("button[type=submit]");

        await Expect(Page.Locator("#signup-name")).ToBeFocusedAsync();
        await Expect(Page.Locator("#signup-name-error")).ToHaveTextAsync("Enter your name.");
        await Expect(Page.Locator("#signup-accept-error")).ToHaveTextAsync("You must accept the terms.");
        await Expect(Page.Locator("#signup-email-error")).ToHaveTextAsync("");
    }

    [Fact]
    public async Task Valid_submit_runs_and_resets_and_server_errors_show()
    {
        await OpenAsync("/signup");
        await Page.Locator("#signup-name").FillAsync("Ada");
        await Page.Locator("#signup-email").FillAsync("taken@example.com");
        await Page.Locator("#signup-password").FillAsync("correct horse");
        await Page.Locator("#signup-confirm").FillAsync("correct horse");
        await Page.Locator("#signup-accept").CheckAsync();
        await Page.ClickAsync("button[type=submit]");

        await Expect(Page.Locator("#signup-email-error")).ToHaveTextAsync("That address is already registered.");

        await Page.Locator("#signup-email").FillAsync("ada@example.com");
        await Page.ClickAsync("button[type=submit]");

        await Expect(Page.Locator(".result")).ToHaveTextAsync("Welcome, Ada!");
        await Expect(Page.Locator("#signup-name")).ToHaveValueAsync("");
        await Expect(Page.Locator("#signup-accept")).Not.ToBeCheckedAsync();
    }
}
