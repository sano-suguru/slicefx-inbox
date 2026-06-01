using System.Text;
using Inbox.Contracts;
using Inbox.Server.Infrastructure;
using SliceFx.Wasi;
using SliceFx.Wasi.HttpClient;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Tests;

public class RefreshFeedsTests
{
    private const string Rss2Feed = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0">
          <channel>
            <title>Test Feed</title>
            <link>https://feed.example.com</link>
            <item>
              <title>Article One</title>
              <link>https://feed.example.com/article-1</link>
              <pubDate>Mon, 01 Jan 2025 00:00:00 GMT</pubDate>
            </item>
            <item>
              <title>Article Two</title>
              <link>https://feed.example.com/article-2</link>
            </item>
          </channel>
        </rss>
        """;

    private const string AtomFeed = """
        <?xml version="1.0" encoding="UTF-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom">
          <title>Atom Test</title>
          <entry>
            <title>Atom Entry</title>
            <link href="https://atom.example.com/entry-1" rel="alternate"/>
          </entry>
        </feed>
        """;

    private static async Task SeedFeedAsync(
        SliceFx.Wasi.WasiApp app, string feedUrl, InMemoryWasiHttpClient http, string rssBody)
    {
        http.Respond(
            r => r.Url == feedUrl,
            new WasiResponse(200,
                new Dictionary<string, string> { ["content-type"] = "application/rss+xml" },
                Encoding.UTF8.GetBytes(rssBody)));

        var body = InboxTestApp.ToJsonBytes(
            new AddFeedRequest { FeedUrl = feedUrl }, InboxJsonContext.Default.AddFeedRequest);
        var resp = await InboxTestApp.MutateAsync(app, "POST", "/api/feeds", body);
        Assert.Equal(200, resp.Status);
    }

    [Fact]
    public async Task RefreshFeeds_imports_items_and_reports_counts()
    {
        var (app, kv, http, _, _) = InboxTestApp.Create();

        await SeedFeedAsync(app, "https://feed.example.com/rss", http, Rss2Feed);

        var response = await InboxTestApp.MutateAsync(app, "POST", "/api/feeds/refresh");
        Assert.Equal(200, response.Status);

        var result = InboxTestApp.FromJsonBody(response, InboxJsonContext.Default.RefreshFeedsResponse)!;
        Assert.Equal(1, result.FeedsChecked);
        Assert.Equal(2, result.ItemsAdded);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(0, result.Failed);

        IKeyValueStore kvStore = kv;
        var items = await KvScan.ListItemsAsync(kvStore, InboxTestApp.DefaultWid, CancellationToken.None);
        Assert.Equal(2, items.Length);
    }

    [Fact]
    public async Task RefreshFeeds_skips_duplicate_urls()
    {
        var (app, _, http, _, _) = InboxTestApp.Create();

        await SeedFeedAsync(app, "https://feed.example.com/rss", http, Rss2Feed);

        await InboxTestApp.MutateAsync(app, "POST", "/api/feeds/refresh");
        var response = await InboxTestApp.MutateAsync(app, "POST", "/api/feeds/refresh");

        var result = InboxTestApp.FromJsonBody(response, InboxJsonContext.Default.RefreshFeedsResponse)!;
        Assert.Equal(0, result.ItemsAdded);
        Assert.Equal(2, result.Skipped);
    }

    [Fact]
    public async Task RefreshFeeds_increments_failed_on_non_2xx_and_continues_batch()
    {
        var (app, _, http, _, _) = InboxTestApp.Create();

        http.Respond(r => r.Url == "https://bad.example.com/rss",
            new WasiResponse(404, new Dictionary<string, string>(), []));
        var body1 = InboxTestApp.ToJsonBytes(
            new AddFeedRequest { FeedUrl = "https://bad.example.com/rss" }, InboxJsonContext.Default.AddFeedRequest);
        await InboxTestApp.MutateAsync(app, "POST", "/api/feeds", body1);

        http.Respond(r => r.Url == "https://good.example.com/rss",
            new WasiResponse(200,
                new Dictionary<string, string> { ["content-type"] = "application/rss+xml" },
                Encoding.UTF8.GetBytes(Rss2Feed)));
        var body2 = InboxTestApp.ToJsonBytes(
            new AddFeedRequest { FeedUrl = "https://good.example.com/rss" }, InboxJsonContext.Default.AddFeedRequest);
        await InboxTestApp.MutateAsync(app, "POST", "/api/feeds", body2);

        var response = await InboxTestApp.MutateAsync(app, "POST", "/api/feeds/refresh");
        var result = InboxTestApp.FromJsonBody(response, InboxJsonContext.Default.RefreshFeedsResponse)!;

        Assert.Equal(2, result.FeedsChecked);
        Assert.Equal(1, result.Failed);
        Assert.Equal(2, result.ItemsAdded);
    }

    [Fact]
    public async Task RefreshFeeds_handles_atom_feed()
    {
        var (app, _, http, _, _) = InboxTestApp.Create();

        await SeedFeedAsync(app, "https://atom.example.com/feed", http, AtomFeed);

        var response = await InboxTestApp.MutateAsync(app, "POST", "/api/feeds/refresh");
        var result = InboxTestApp.FromJsonBody(response, InboxJsonContext.Default.RefreshFeedsResponse)!;

        Assert.Equal(1, result.FeedsChecked);
        Assert.Equal(1, result.ItemsAdded);
    }

    [Fact]
    public async Task RefreshAllWorkspacesAsync_refreshes_multiple_workspaces()
    {
        // Test the all-workspace orchestrator directly (FeedRefreshCronHandler is compile-removed from non-WASI builds)
        var (_, kv, http, _, _) = InboxTestApp.Create();

        // Seed a second workspace
        const string wid2 = "test-wid-2";
        const string token2 = "test-token-2";
        await InboxTestApp.SeedWorkspaceAsync(kv, token2, wid2);

        // Add a feed to each workspace directly in KV
        var feedId1 = Guid.NewGuid().ToString("N");
        var feedId2 = Guid.NewGuid().ToString("N");
        var feed1 = new FeedSubscription(feedId1, "https://feed1.example.com/rss", null, DateTimeOffset.UtcNow);
        var feed2 = new FeedSubscription(feedId2, "https://feed2.example.com/rss", null, DateTimeOffset.UtcNow);

        IKeyValueStore kvStore = kv;
        // Write feed bodies only — no index keys needed; KvScan derives listings by prefix scan.
        await kvStore.SetJsonAsync(WorkspaceKeys.Feed(InboxTestApp.DefaultWid, feedId1), feed1, InboxJsonContext.Default.FeedSubscription, CancellationToken.None);
        await kvStore.SetJsonAsync(WorkspaceKeys.Feed(wid2, feedId2), feed2, InboxJsonContext.Default.FeedSubscription, CancellationToken.None);

        // Stub both feed URLs
        http.Respond(r => r.Url == "https://feed1.example.com/rss",
            new WasiResponse(200,
                new Dictionary<string, string> { ["content-type"] = "application/rss+xml" },
                System.Text.Encoding.UTF8.GetBytes(Rss2Feed)));
        http.Respond(r => r.Url == "https://feed2.example.com/rss",
            new WasiResponse(200,
                new Dictionary<string, string> { ["content-type"] = "application/rss+xml" },
                System.Text.Encoding.UTF8.GetBytes(AtomFeed)));

        // Call the orchestrator directly
        var result = await Inbox.Server.Features.Feeds.RefreshFeeds.RefreshAllWorkspacesAsync(http, kv, CancellationToken.None);

        Assert.Equal(2, result.FeedsChecked);
        Assert.Equal(3, result.ItemsAdded); // 2 from RSS feed + 1 from Atom feed

        // Each workspace should have the expected items via prefix scan.
        var items1 = await KvScan.ListItemsAsync(kvStore, InboxTestApp.DefaultWid, CancellationToken.None);
        var items2 = await KvScan.ListItemsAsync(kvStore, wid2, CancellationToken.None);
        Assert.Equal(2, items1.Length);
        Assert.Single(items2);
    }
}
