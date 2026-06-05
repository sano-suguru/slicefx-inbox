using System.Text;
using Inbox.Contracts;
using Inbox.Server.Infrastructure;
using SliceFx.Wasi;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Tests;

/// <summary>
/// Tests for per-workspace resource limits (Fix 2).
/// </summary>
public class ResourceLimitTests
{
    // ── AddFeed limits ──────────────────────────────────────────────────────

    [Fact]
    public async Task AddFeed_returns_429_when_feed_limit_reached()
    {
        var (app, kv, _, _, _) = InboxTestApp.Create();
        IKeyValueStore kvStore = kv;

        // Seed MaxFeedsPerWorkspace feed keys directly (no body needed — CountFeedKeysAsync counts keys only)
        for (var i = 0; i < Inbox.Server.Features.Feeds.AddFeed.MaxFeedsPerWorkspace; i++)
        {
            var feed = new FeedSubscription($"feed-{i}", $"https://feed-{i}.example.com/rss", null, DateTimeOffset.UtcNow);
            await kvStore.SetJsonAsync(
                WorkspaceKeys.Feed(InboxTestApp.DefaultWid, $"feed-{i}"),
                feed, InboxJsonContext.Default.FeedSubscription, CancellationToken.None);
        }

        var body = InboxTestApp.ToJsonBytes(
            new AddFeedRequest { FeedUrl = "https://one-more.example.com/rss" },
            InboxJsonContext.Default.AddFeedRequest);
        var response = await InboxTestApp.MutateAsync(app, "POST", "/api/feeds", body);

        Assert.Equal(429, response.Status);
    }

    [Fact]
    public async Task AddFeed_returns_400_for_http_url()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();

        var body = InboxTestApp.ToJsonBytes(
            new AddFeedRequest { FeedUrl = "http://insecure.example.com/rss" },
            InboxJsonContext.Default.AddFeedRequest);
        var response = await InboxTestApp.MutateAsync(app, "POST", "/api/feeds", body);

        Assert.Equal(400, response.Status);
    }

    [Fact]
    public async Task AddFeed_returns_403_for_demo_workspace()
    {
        var (app, kv, _, _, _) = InboxTestApp.Create();

        // Seed the demo workspace
        await InboxTestApp.SeedWorkspaceAsync(kv, DemoWorkspace.Token, DemoWorkspace.Wid);

        var body = InboxTestApp.ToJsonBytes(
            new AddFeedRequest { FeedUrl = "https://example.com/rss" },
            InboxJsonContext.Default.AddFeedRequest);
        var response = await InboxTestApp.MutateAsync(app, "POST", "/api/feeds", body, DemoWorkspace.Token);

        Assert.Equal(403, response.Status);
    }

    // ── PostItem https check ────────────────────────────────────────────────

    [Fact]
    public async Task PostItem_returns_400_for_http_url()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();

        var body = InboxTestApp.ToJsonBytes(
            new PostItemRequest { Url = "http://insecure.example.com/article" },
            InboxJsonContext.Default.PostItemRequest);
        var response = await InboxTestApp.MutateAsync(app, "POST", "/api/items", body);

        Assert.Equal(400, response.Status);
    }

    // ── UpdateItem tag limits ────────────────────────────────────────────────

    [Fact]
    public async Task UpdateItem_returns_400_for_too_many_tags()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();

        // Create an item first
        var itemBody = InboxTestApp.ToJsonBytes(
            new PostItemRequest { Url = "https://example.com" }, InboxJsonContext.Default.PostItemRequest);
        var posted = InboxTestApp.FromJsonBody(
            await InboxTestApp.MutateAsync(app, "POST", "/api/items", itemBody),
            InboxJsonContext.Default.PostItemResponse)!;

        // Try to update with too many tags (21)
        var tags = Enumerable.Range(0, 21).Select(i => $"tag{i}").ToArray();
        var updateBody = InboxTestApp.ToJsonBytes(
            new UpdateItemRequest { Tags = tags }, InboxJsonContext.Default.UpdateItemRequest);
        var response = await InboxTestApp.MutateAsync(app, "PATCH", $"/api/items/{posted.Id}", updateBody);

        Assert.Equal(400, response.Status);
    }

    [Fact]
    public async Task UpdateItem_returns_400_for_tag_too_long()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();

        var itemBody = InboxTestApp.ToJsonBytes(
            new PostItemRequest { Url = "https://example.com" }, InboxJsonContext.Default.PostItemRequest);
        var posted = InboxTestApp.FromJsonBody(
            await InboxTestApp.MutateAsync(app, "POST", "/api/items", itemBody),
            InboxJsonContext.Default.PostItemResponse)!;

        var updateBody = InboxTestApp.ToJsonBytes(
            new UpdateItemRequest { Tags = [new string('a', 101)] }, InboxJsonContext.Default.UpdateItemRequest);
        var response = await InboxTestApp.MutateAsync(app, "PATCH", $"/api/items/{posted.Id}", updateBody);

        Assert.Equal(400, response.Status);
    }

    [Fact]
    public async Task UpdateItem_accepts_max_allowed_tags()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();

        var itemBody = InboxTestApp.ToJsonBytes(
            new PostItemRequest { Url = "https://example.com" }, InboxJsonContext.Default.PostItemRequest);
        var posted = InboxTestApp.FromJsonBody(
            await InboxTestApp.MutateAsync(app, "POST", "/api/items", itemBody),
            InboxJsonContext.Default.PostItemResponse)!;

        // Exactly 20 tags of 100 chars each → should succeed
        var tags = Enumerable.Range(0, 20).Select(i => new string((char)('a' + (i % 26)), 100)).ToArray();
        var updateBody = InboxTestApp.ToJsonBytes(
            new UpdateItemRequest { Tags = tags }, InboxJsonContext.Default.UpdateItemRequest);
        var response = await InboxTestApp.MutateAsync(app, "PATCH", $"/api/items/{posted.Id}", updateBody);

        Assert.Equal(204, response.Status);
    }

    // ── RefreshFeeds entry cap ────────────────────────────────────────────────

    [Fact]
    public async Task RefreshFeeds_caps_entries_per_feed()
    {
        var (app, kv, http, _, _) = InboxTestApp.Create();

        // Build a feed with MaxEntriesPerRefresh + 5 entries
        var cap = Inbox.Server.Features.Feeds.RefreshFeeds.MaxEntriesPerRefresh;
        var entries = string.Concat(
            Enumerable.Range(0, cap + 5)
                .Select(i => $"<item><title>T{i}</title><link>https://big.example.com/item-{i}</link></item>"));
        var rss = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <rss version="2.0"><channel>{entries}</channel></rss>
            """;

        http.Respond(r => r.Url == "https://big.example.com/rss",
            new SliceFx.Wasi.WasiResponse(200,
                new Dictionary<string, string> { ["content-type"] = "application/rss+xml" },
                Encoding.UTF8.GetBytes(rss)));

        var feedBody = InboxTestApp.ToJsonBytes(
            new AddFeedRequest { FeedUrl = "https://big.example.com/rss" }, InboxJsonContext.Default.AddFeedRequest);
        await InboxTestApp.MutateAsync(app, "POST", "/api/feeds", feedBody);

        var response = await InboxTestApp.MutateAsync(app, "POST", "/api/feeds/refresh");
        var result = InboxTestApp.FromJsonBody(response, InboxJsonContext.Default.RefreshFeedsResponse)!;

        // Must not exceed the per-feed cap
        Assert.Equal(cap, result.ItemsAdded);
    }
}
