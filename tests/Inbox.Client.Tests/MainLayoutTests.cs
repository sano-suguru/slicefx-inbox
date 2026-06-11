using Bunit;
using Inbox.Client;
using Inbox.Client.Layout;
using Inbox.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Inbox.Client.Tests;

public class MainLayoutTests : BunitContext
{
    // MainLayout requires LayoutComponentBase parameters — wrap with a dummy @Body content.
    private const string BodyContent = "<p id='body-content'>body</p>";

    private void SetupServices(string? initialToken = null)
    {
        this.AddTokenHolder(initialToken);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    // ── Route guard ───────────────────────────────────────────────────

    [Fact]
    public async Task No_token_navigates_to_login()
    {
        // No initial token
        SetupServices(initialToken: null);

        var nav = Services.GetRequiredService<NavigationManager>();

        Render<MainLayout>(p => p.Add(c => c.Body, BodyContent));

        // Give OnInitializedAsync time to complete
        await Task.Delay(50);

        Assert.Contains("/login", nav.Uri);
    }

    [Fact]
    public async Task Already_on_login_no_redirect()
    {
        SetupServices(initialToken: null);

        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/login");

        Render<MainLayout>(p => p.Add(c => c.Body, BodyContent));

        await Task.Delay(50);

        // Should stay on /login, not redirect to /login again with reason=expired
        Assert.DoesNotContain("reason=expired", nav.Uri);
        Assert.Contains("/login", nav.Uri);
    }

    // ── Demo mode banner ──────────────────────────────────────────────

    [Fact]
    public void Demo_token_shows_demo_banner()
    {
        SetupServices(initialToken: DemoWorkspace.Token);

        var cut = Render<MainLayout>(p => p.Add(c => c.Body, BodyContent));

        cut.WaitForAssertion(() =>
            Assert.Contains("demo workspace", cut.Markup, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Non_demo_token_does_not_show_demo_banner()
    {
        SetupServices(initialToken: "regular-token");

        var cut = Render<MainLayout>(p => p.Add(c => c.Body, BodyContent));

        cut.WaitForAssertion(() =>
            Assert.DoesNotContain("demo workspace", cut.Markup, StringComparison.OrdinalIgnoreCase));
    }
}
