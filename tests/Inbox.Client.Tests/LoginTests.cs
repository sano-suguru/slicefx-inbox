using System.Net;
using Bunit;
using Inbox.Client;
using Inbox.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Inbox.Client.Tests;

public class LoginPageTests : BunitContext
{
    private void SetupServices(SliceApiClient api)
    {
        this.AddTokenHolder();
        Services.AddSingleton(api);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    // ── ?reason=expired banner ────────────────────────────────────────

    [Fact]
    public void Shows_expired_banner_when_reason_is_expired()
    {
        var stub = new StubHttpHandler();
        SetupServices(stub.BuildClient());

        // Navigate to the login page with the reason query param BEFORE rendering
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/login?reason=expired");

        var cut = Render<Inbox.Client.Pages.Login>();

        Assert.Contains("session expired", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Does_not_show_expired_banner_without_reason()
    {
        var stub = new StubHttpHandler();
        SetupServices(stub.BuildClient());

        var cut = Render<Inbox.Client.Pages.Login>();

        Assert.DoesNotContain("session expired", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    // ── CreateWorkspace 403 / 429 error messages ──────────────────────

    [Fact]
    public async Task CreateWorkspace_403_shows_registration_closed_message()
    {
        var stub = new StubHttpHandler()
            .RespondWithProblem(HttpStatusCode.Forbidden, 403, "Forbidden");
        SetupServices(stub.BuildClient());

        var cut = Render<Inbox.Client.Pages.Login>();

        // "Create workspace" button is the first btn-primary on the page
        await cut.Find("button.btn-primary").ClickAsync(new());

        cut.WaitForAssertion(() =>
            Assert.Contains("registration is currently closed", cut.Markup, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreateWorkspace_429_shows_limit_reached_message()
    {
        var stub = new StubHttpHandler()
            .RespondWithProblem(HttpStatusCode.TooManyRequests, 429, "Too Many Requests");
        SetupServices(stub.BuildClient());

        var cut = Render<Inbox.Client.Pages.Login>();

        await cut.Find("button.btn-primary").ClickAsync(new());

        cut.WaitForAssertion(() =>
            Assert.Contains("Workspace limit reached", cut.Markup));
    }

    // ── Paste token: inline 401 shows "Invalid token." not redirect ───
    // This path must NOT call AuthRedirect (which would navigate to /login?reason=expired).

    [Fact]
    public async Task EnterWithPastedToken_401_shows_invalid_token_not_redirect()
    {
        var stub = new StubHttpHandler()
            .RespondWith(HttpResponseFactory.Unauthorized());
        SetupServices(stub.BuildClient());

        var nav = Services.GetRequiredService<NavigationManager>();

        var cut = Render<Inbox.Client.Pages.Login>();

        // @bind uses "change" event — ChangeAsync triggers the binding update
        await cut.Find("#paste-token").ChangeAsync(new() { Value = "some-token" });

        // Find the Enter button (in the paste section — second card on the page)
        var enterBtn = cut.FindAll("button").First(b =>
            b.TextContent.Trim() is "Enter" or "Verifying…");
        await enterBtn.ClickAsync(new());

        // Should show "Invalid token." error inline
        cut.WaitForAssertion(() =>
            Assert.Contains("Invalid token", cut.Markup, StringComparison.OrdinalIgnoreCase));

        // Must NOT navigate to /login?reason=expired (that would be AuthRedirect's path)
        Assert.DoesNotContain("reason=expired", nav.Uri);
    }
}
