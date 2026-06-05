using System.Text;  // Encoding for RSS feed bytes
using Inbox.Contracts;
using Inbox.Server.Infrastructure;
using SliceFx.Wasi;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Tests;

/// <summary>
/// Regression tests for corrupt KV blob tolerance (Fix 1).
/// Verifies that a malformed JSON blob does not abort item/feed listings or the cron sweep.
/// </summary>
public class CorruptBlobTests
{
    [Fact]
    public async Task ListItemsAsync_skips_corrupt_blob_and_returns_valid_items()
    {
        var (_, kv, _, _, _) = InboxTestApp.Create();
        IKeyValueStore kvStore = kv;

        // Write one valid item
        var validItem = new InboxItem("valid-id", "https://example.com", "Valid", null, ItemStatus.Unread, DateTimeOffset.UtcNow, "bookmark");
        await kvStore.SetJsonAsync(
            WorkspaceKeys.Item(InboxTestApp.DefaultWid, "valid-id"),
            validItem, InboxJsonContext.Default.InboxItem, CancellationToken.None);

        // Write a corrupt blob under an item key
        var corruptKey = WorkspaceKeys.Item(InboxTestApp.DefaultWid, "corrupt-id");
        await kvStore.SetStringAsync(corruptKey, "{not valid json!!!", CancellationToken.None);

        // Listing must succeed and contain only the valid item
        var items = await KvScan.ListItemsAsync(kvStore, InboxTestApp.DefaultWid, CancellationToken.None);
        Assert.Contains(items, i => i.Id == "valid-id");
        Assert.DoesNotContain(items, i => i.Id == "corrupt-id");
    }

    [Fact]
    public async Task ListFeedsAsync_skips_corrupt_blob_and_returns_valid_feeds()
    {
        var (_, kv, _, _, _) = InboxTestApp.Create();
        IKeyValueStore kvStore = kv;

        var validFeed = new FeedSubscription("valid-feed", "https://example.com/rss", null, DateTimeOffset.UtcNow);
        await kvStore.SetJsonAsync(
            WorkspaceKeys.Feed(InboxTestApp.DefaultWid, "valid-feed"),
            validFeed, InboxJsonContext.Default.FeedSubscription, CancellationToken.None);

        var corruptKey = WorkspaceKeys.Feed(InboxTestApp.DefaultWid, "corrupt-feed");
        await kvStore.SetStringAsync(corruptKey, "}}}}not-json{{{{", CancellationToken.None);

        var feeds = await KvScan.ListFeedsAsync(kvStore, InboxTestApp.DefaultWid, CancellationToken.None);
        Assert.Contains(feeds, f => f.Id == "valid-feed");
        Assert.DoesNotContain(feeds, f => f.Id == "corrupt-feed");
    }

    [Fact]
    public async Task GetItems_endpoint_survives_corrupt_item_blob()
    {
        var (app, kv, _, _, _) = InboxTestApp.Create();
        IKeyValueStore kvStore = kv;

        // Seed a corrupt item blob directly
        await kvStore.SetStringAsync(
            WorkspaceKeys.Item(InboxTestApp.DefaultWid, "bad"),
            "not-json",
            CancellationToken.None);

        // GET /api/items must return 200 (not 500), skipping the corrupt entry
        var response = await InboxTestApp.GetAsync(app, "/api/items");
        Assert.Equal(200, response.Status);
    }

    [Fact]
    public async Task RefreshAllWorkspacesAsync_continues_after_corrupt_workspace_blob()
    {
        var (_, kv, http, _, _) = InboxTestApp.Create();
        IKeyValueStore kvStore = kv;

        // Seed a second workspace with a valid feed so we can detect it was processed
        const string wid2 = "healthy-wid";
        const string token2 = "healthy-token";
        await InboxTestApp.SeedWorkspaceAsync(kv, token2, wid2);

        // Add a valid feed to the healthy workspace
        const string feedUrl = "https://healthy.example.com/rss";
        const string rss = """
            <?xml version="1.0" encoding="UTF-8"?>
            <rss version="2.0"><channel>
              <item><title>A</title><link>https://healthy.example.com/a</link></item>
            </channel></rss>
            """;
        var feed = new FeedSubscription("hfeed", feedUrl, null, DateTimeOffset.UtcNow);
        await kvStore.SetJsonAsync(
            WorkspaceKeys.Feed(wid2, "hfeed"), feed,
            InboxJsonContext.Default.FeedSubscription, CancellationToken.None);

        // Plant a corrupt item blob in the default workspace so its loading phase throws
        await kvStore.SetStringAsync(
            WorkspaceKeys.Item(InboxTestApp.DefaultWid, "bad"),
            "}}corrupt{{",
            CancellationToken.None);

        http.Respond(r => r.Url == feedUrl,
            new SliceFx.Wasi.WasiResponse(200,
                new Dictionary<string, string> { ["content-type"] = "application/rss+xml" },
                Encoding.UTF8.GetBytes(rss)));

        // The sweep must not throw and must process the healthy workspace
        var result = await Inbox.Server.Features.Feeds.RefreshFeeds.RefreshAllWorkspacesAsync(
            http, kv, CancellationToken.None);

        // healthy workspace: 1 feed checked, 1 item added
        Assert.True(result.FeedsChecked >= 1, "healthy workspace feed should have been checked");
        Assert.True(result.ItemsAdded >= 1, "healthy workspace item should have been added");
    }
}
