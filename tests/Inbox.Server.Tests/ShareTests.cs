using System.Text;
using Inbox.Contracts;
using Inbox.Server.Infrastructure;
using SliceFx.Wasi;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Tests;

/// <summary>
/// Tests for the share-page feature set:
///   GET  /s/{token}             — public, no auth, returns HTML
///   POST /api/items/{id}/share  — authenticated, creates share link
///   DELETE /api/items/{id}/share — authenticated, revokes share link
/// and the DeleteItem share-cleanup side-effect.
/// </summary>
public class ShareTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    private const string DefaultUrl = "https://example.com/article";

    /// <summary>Creates an item and returns its ID.</summary>
    private static async Task<string> CreateItemAsync(WasiApp app, string url = DefaultUrl)
    {
        var body = InboxTestApp.ToJsonBytes(
            new PostItemRequest { Url = url }, InboxJsonContext.Default.PostItemRequest);
        var resp = InboxTestApp.FromJsonBody(
            await InboxTestApp.MutateAsync(app, "POST", "/api/items", body),
            InboxJsonContext.Default.PostItemResponse)!;
        return resp.Id;
    }

    /// <summary>POSTs to /api/items/{id}/share and returns the WasiResponse.</summary>
    private static Task<WasiResponse> CreateShareAsync(WasiApp app, string id, string token = InboxTestApp.DefaultToken)
        => InboxTestApp.MutateAsync(app, "POST", $"/api/items/{id}/share", token: token);

    /// <summary>DELETEs /api/items/{id}/share and returns the WasiResponse.</summary>
    private static Task<WasiResponse> RevokeShareAsync(WasiApp app, string id, string token = InboxTestApp.DefaultToken)
        => InboxTestApp.MutateAsync(app, "DELETE", $"/api/items/{id}/share", token: token);

    /// <summary>GETs /s/{shareToken} without authentication (public page).</summary>
    private static Task<WasiResponse> GetSharePageAsync(WasiApp app, string shareToken)
        => app.DispatchAsync(new WasiRequest("GET", $"/s/{shareToken}",
            new Dictionary<string, string>(), null, null));

    private static string BodyText(WasiResponse resp) => Encoding.UTF8.GetString(resp.Body);

    // ── Create share ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateShare_returns_200_with_token()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();
        var id = await CreateItemAsync(app);

        var resp = await CreateShareAsync(app, id);

        Assert.Equal(200, resp.Status);
        var share = InboxTestApp.FromJsonBody(resp, InboxJsonContext.Default.ShareResponse)!;
        Assert.False(string.IsNullOrEmpty(share.ShareToken));
    }

    [Fact]
    public async Task CreateShare_is_idempotent_returns_same_token()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();
        var id = await CreateItemAsync(app);

        var first = InboxTestApp.FromJsonBody(
            await CreateShareAsync(app, id), InboxJsonContext.Default.ShareResponse)!;
        var second = InboxTestApp.FromJsonBody(
            await CreateShareAsync(app, id), InboxJsonContext.Default.ShareResponse)!;

        Assert.Equal(first.ShareToken, second.ShareToken);
    }

    [Fact]
    public async Task CreateShare_returns_401_without_token()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();
        var id = await CreateItemAsync(app);

        var resp = await InboxTestApp.MutateAsync(app, "POST", $"/api/items/{id}/share", token: "invalid-token");

        Assert.Equal(401, resp.Status);
    }

    [Fact]
    public async Task CreateShare_returns_404_for_nonexistent_item()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();

        var resp = await CreateShareAsync(app, "does-not-exist");

        Assert.Equal(404, resp.Status);
    }

    // ── Public share page (GET /s/{token}) ────────────────────────────────────

    [Fact]
    public async Task GetSharePage_returns_200_html_for_valid_share()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();
        var id = await CreateItemAsync(app, DefaultUrl);
        var share = InboxTestApp.FromJsonBody(
            await CreateShareAsync(app, id), InboxJsonContext.Default.ShareResponse)!;

        var resp = await GetSharePageAsync(app, share.ShareToken);

        Assert.Equal(200, resp.Status);
        Assert.True(resp.Headers.TryGetValue("Content-Type", out var ct));
        Assert.StartsWith("text/html", ct);
        var body = BodyText(resp);
        Assert.Contains(DefaultUrl, body); // URL appears in the page
    }

    [Fact]
    public async Task GetSharePage_returns_404_for_unknown_token()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();

        var resp = await GetSharePageAsync(app, "nonexistent-token");

        Assert.Equal(404, resp.Status);
        Assert.True(resp.Headers.TryGetValue("Content-Type", out var ct));
        Assert.StartsWith("text/html", ct);
        Assert.True(resp.Headers.TryGetValue("Cache-Control", out var cc));
        Assert.Contains("no-store", cc);
    }

    [Fact]
    public async Task GetSharePage_includes_no_store_on_ok_response()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();
        var id = await CreateItemAsync(app);
        var share = InboxTestApp.FromJsonBody(
            await CreateShareAsync(app, id), InboxJsonContext.Default.ShareResponse)!;

        var resp = await GetSharePageAsync(app, share.ShareToken);

        Assert.True(resp.Headers.TryGetValue("Cache-Control", out var cc));
        Assert.Contains("no-store", cc);
    }

    [Fact]
    public async Task GetSharePage_includes_og_url_with_base_url()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();
        var id = await CreateItemAsync(app);
        var share = InboxTestApp.FromJsonBody(
            await CreateShareAsync(app, id), InboxJsonContext.Default.ShareResponse)!;

        var resp = await GetSharePageAsync(app, share.ShareToken);

        var body = BodyText(resp);
        // InboxTestApp sets public_base_url = "https://example.test"
        Assert.Contains("https://example.test/s/", body);
    }

    // ── Revoke ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RevokeShare_makes_page_return_404()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();
        var id = await CreateItemAsync(app);
        var share = InboxTestApp.FromJsonBody(
            await CreateShareAsync(app, id), InboxJsonContext.Default.ShareResponse)!;

        var revoke = await RevokeShareAsync(app, id);
        Assert.Equal(204, revoke.Status);

        var page = await GetSharePageAsync(app, share.ShareToken);
        Assert.Equal(404, page.Status);
    }

    [Fact]
    public async Task RevokeShare_is_idempotent()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();
        var id = await CreateItemAsync(app);
        await CreateShareAsync(app, id);

        var first = await RevokeShareAsync(app, id);
        var second = await RevokeShareAsync(app, id);

        Assert.Equal(204, first.Status);
        Assert.Equal(204, second.Status);
    }

    [Fact]
    public async Task RevokeShare_returns_401_without_token()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();
        var id = await CreateItemAsync(app);
        await CreateShareAsync(app, id);

        var resp = await InboxTestApp.MutateAsync(app, "DELETE", $"/api/items/{id}/share", token: "wrong");

        Assert.Equal(401, resp.Status);
    }

    // ── DeleteItem share cleanup ────────────────────────────────────────────────

    [Fact]
    public async Task DeleteItem_cleans_up_share_keys()
    {
        var (app, kv, _, _, _) = InboxTestApp.Create();
        var id = await CreateItemAsync(app);
        var share = InboxTestApp.FromJsonBody(
            await CreateShareAsync(app, id), InboxJsonContext.Default.ShareResponse)!;

        // Delete the item.
        var del = await InboxTestApp.MutateAsync(app, "DELETE", $"/api/items/{id}");
        Assert.Equal(204, del.Status);

        // Share page must now return 404.
        var page = await GetSharePageAsync(app, share.ShareToken);
        Assert.Equal(404, page.Status);

        // Both KV keys must be gone.
        Assert.False(await ((IKeyValueStore)kv).ExistsAsync(
            WorkspaceKeys.Share(share.ShareToken), CancellationToken.None));
        Assert.False(await ((IKeyValueStore)kv).ExistsAsync(
            WorkspaceKeys.ItemShare(InboxTestApp.DefaultWid, id), CancellationToken.None));
    }

    [Fact]
    public async Task DeleteItem_without_share_succeeds()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();
        var id = await CreateItemAsync(app);

        // Delete without ever sharing — must not throw.
        var resp = await InboxTestApp.MutateAsync(app, "DELETE", $"/api/items/{id}");

        Assert.Equal(204, resp.Status);
    }

    // ── Security: XSS and information leakage ─────────────────────────────────

    [Fact]
    public async Task SharePage_escapes_xss_in_title()
    {
        var (app, kv, _, _, _) = InboxTestApp.Create();

        // Seed an item with a malicious title directly in KV (bypassing PostItem scraping).
        const string maliciousTitle = "<script>alert(1)</script>";
        const string itemId = "xss-test-id";
        var item = new InboxItem(itemId, "https://example.com", maliciousTitle, null,
            ItemStatus.Unread, DateTimeOffset.UtcNow, "bookmark");
        await ((IKeyValueStore)kv).SetJsonAsync(
            WorkspaceKeys.Item(InboxTestApp.DefaultWid, itemId),
            item, InboxJsonContext.Default.InboxItem, CancellationToken.None);

        // Seed the share keys directly.
        const string shareToken = "xss-share-token";
        await ((IKeyValueStore)kv).SetStringAsync(
            WorkspaceKeys.Share(shareToken),
            $"{InboxTestApp.DefaultWid}:{itemId}", CancellationToken.None);

        var resp = await GetSharePageAsync(app, shareToken);
        Assert.Equal(200, resp.Status);

        var body = BodyText(resp);
        Assert.Contains("&lt;script&gt;", body);
        Assert.DoesNotContain("<script>", body);
    }

    [Fact]
    public async Task SharePage_does_not_leak_wid_or_itemId()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();
        var id = await CreateItemAsync(app);
        var share = InboxTestApp.FromJsonBody(
            await CreateShareAsync(app, id), InboxJsonContext.Default.ShareResponse)!;

        var resp = await GetSharePageAsync(app, share.ShareToken);
        var body = BodyText(resp);

        // wid and itemId must NOT appear in the rendered body.
        Assert.DoesNotContain(InboxTestApp.DefaultWid, body);
        Assert.DoesNotContain(id, body);
    }

    // ── HtmlPage.Escape unit tests ────────────────────────────────────────────

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("hello", "hello")]
    [InlineData("<b>bold</b>", "&lt;b&gt;bold&lt;/b&gt;")]
    [InlineData("A & B", "A &amp; B")]
    [InlineData("say \"hi\"", "say &quot;hi&quot;")]
    [InlineData("it's fine", "it&#39;s fine")]
    [InlineData("<script>alert('xss')</script>", "&lt;script&gt;alert(&#39;xss&#39;)&lt;/script&gt;")]
    public void HtmlPage_Escape_encodes_special_chars(string? input, string expected)
    {
        Assert.Equal(expected, HtmlPage.Escape(input));
    }

    // ── WorkspaceKeys.ParseShare unit tests ──────────────────────────────────

    [Fact]
    public void ParseShare_returns_wid_and_itemId_for_valid_value()
    {
        var result = WorkspaceKeys.ParseShare("mywid:myitemid");
        Assert.NotNull(result);
        Assert.Equal("mywid", result!.Value.Wid);
        Assert.Equal("myitemid", result!.Value.ItemId);
    }

    [Fact]
    public void ParseShare_handles_colon_in_itemId()
    {
        // ItemIds are Guids (no colons) but ParseShare should not break if one appears.
        var result = WorkspaceKeys.ParseShare("wid:a:b:c");
        Assert.NotNull(result);
        Assert.Equal("wid", result!.Value.Wid);
        Assert.Equal("a:b:c", result!.Value.ItemId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(":nowidpart")]
    [InlineData("nowidsep")]
    [InlineData("wid:")]
    public void ParseShare_returns_null_for_malformed_value(string? value)
    {
        Assert.Null(WorkspaceKeys.ParseShare(value));
    }
}
