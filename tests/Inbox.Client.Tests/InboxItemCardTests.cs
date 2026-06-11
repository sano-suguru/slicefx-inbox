using Bunit;
using Inbox.Client;
using Inbox.Client.Components;
using Inbox.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Inbox.Client.Tests;

public class InboxItemCardTests : BunitContext
{
    private static InboxItem MakeItem(
        string id = "item-1",
        string url = "https://example.com",
        string title = "Test Title",
        string status = ItemStatus.Unread,
        string[]? tags = null) =>
        new(id, url, title, null, status, DateTimeOffset.UtcNow, "bookmark", tags);

    private void SetupServices(SliceApiClient api)
    {
        this.AddTokenHolder("test-token");
        Services.AddSingleton(api);
    }

    // ── StatusBadgeClass ──────────────────────────────────────────────

    [Theory]
    [InlineData(ItemStatus.Unread, "badge-unread")]
    [InlineData(ItemStatus.Read, "badge-read")]
    [InlineData(ItemStatus.Archived, "badge-archived")]
    public void Status_badge_maps_to_correct_css_class(string status, string expectedClass)
    {
        var stub = new StubHttpHandler();
        SetupServices(stub.BuildClient());

        var cut = Render<InboxItemCard>(p =>
            p.Add(c => c.Item, MakeItem(status: status)));

        Assert.Contains(expectedClass, cut.Find(".badge").ClassList);
    }

    // ── OnStatusChanged callback ──────────────────────────────────────

    [Fact]
    public async Task SetStatus_fires_OnStatusChanged_with_new_status()
    {
        var stub = new StubHttpHandler()
            .RespondWith(HttpResponseFactory.NoContent());
        SetupServices(stub.BuildClient());

        StatusChange? received = null;
        var cut = Render<InboxItemCard>(p =>
        {
            p.Add(c => c.Item, MakeItem(status: ItemStatus.Unread));
            p.Add(c => c.OnStatusChanged, sc => received = sc);
        });

        await cut.Find("[aria-label*='Mark'][aria-label*='read']").ClickAsync(new());

        Assert.NotNull(received);
        Assert.Equal("item-1", received!.Id);
        Assert.Equal(ItemStatus.Read, received.Status);
    }

    // ── Delete confirm / cancel two-step ─────────────────────────────

    [Fact]
    public void Delete_button_shows_confirm_prompt_on_first_click()
    {
        var stub = new StubHttpHandler();
        SetupServices(stub.BuildClient());

        var cut = Render<InboxItemCard>(p =>
            p.Add(c => c.Item, MakeItem()));

        cut.Find("[aria-label*='Delete']").Click();

        Assert.Contains("Delete this item?", cut.Markup);
    }

    [Fact]
    public void CancelDelete_hides_confirm_prompt()
    {
        var stub = new StubHttpHandler();
        SetupServices(stub.BuildClient());

        var cut = Render<InboxItemCard>(p =>
            p.Add(c => c.Item, MakeItem()));

        cut.Find("[aria-label*='Delete']").Click();
        cut.Find(".btn-secondary").Click(); // Cancel button in confirm row

        Assert.DoesNotContain("Delete this item?", cut.Markup);
    }

    [Fact]
    public async Task ConfirmDelete_fires_OnDeleted_with_item_id()
    {
        var stub = new StubHttpHandler()
            .RespondWith(HttpResponseFactory.NoContent());
        SetupServices(stub.BuildClient());

        string? deletedId = null;
        var cut = Render<InboxItemCard>(p =>
        {
            p.Add(c => c.Item, MakeItem(id: "del-123"));
            p.Add(c => c.OnDeleted, id => deletedId = id);
        });

        cut.Find("[aria-label*='Delete']").Click();
        await cut.Find(".btn-danger").ClickAsync(new()); // "Delete" in confirm row

        Assert.Equal("del-123", deletedId);
    }

    // ── 401 → AuthRedirect ────────────────────────────────────────────

    [Fact]
    public async Task SetStatus_401_navigates_to_login_expired()
    {
        var stub = new StubHttpHandler()
            .RespondWith(HttpResponseFactory.Unauthorized());
        SetupServices(stub.BuildClient());

        var cut = Render<InboxItemCard>(p =>
            p.Add(c => c.Item, MakeItem(status: ItemStatus.Unread)));

        await cut.Find("[aria-label*='Mark'][aria-label*='read']").ClickAsync(new());

        var nav = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        Assert.Equal("http://localhost/login?reason=expired", nav.Uri);
    }

    // ── Share clipboard fallback ──────────────────────────────────────

    [Fact]
    public async Task Share_shows_fallback_url_when_clipboard_js_throws()
    {
        var shareToken = "abc123";
        var stub = new StubHttpHandler()
            .RespondWith(HttpResponseFactory.Json(new ShareResponse(shareToken)));
        SetupServices(stub.BuildClient());

        // Clipboard API throws (simulates non-Secure Context / denied permission)
        JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true)
                 .SetException(new Microsoft.JSInterop.JSException("clipboard denied"));

        var cut = Render<InboxItemCard>(p =>
            p.Add(c => c.Item, MakeItem(id: "share-item")));

        await cut.Find("[aria-label*='Share']").ClickAsync(new());

        // Fallback: the share URL is shown as a link
        Assert.Contains($"/s/{shareToken}", cut.Markup);
    }

    [Fact]
    public async Task Share_shows_copied_message_when_clipboard_succeeds()
    {
        var shareToken = "tok456";
        var stub = new StubHttpHandler()
            .RespondWith(HttpResponseFactory.Json(new ShareResponse(shareToken)));
        SetupServices(stub.BuildClient());

        // Use Loose mode so unregistered JS calls return defaults (clipboard succeeds without throwing)
        JSInterop.Mode = Bunit.JSRuntimeMode.Loose;

        var cut = Render<InboxItemCard>(p =>
            p.Add(c => c.Item, MakeItem(id: "share-item-2")));

        await cut.Find("[aria-label*='Share']").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.Contains("Copied:", cut.Markup));
    }
}
