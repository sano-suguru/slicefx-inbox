using System.Text;
using Inbox.Contracts;
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
        var (app, kv, http, _) = InboxTestApp.Create();

        await SeedFeedAsync(app, "https://feed.example.com/rss", http, Rss2Feed);

        var response = await InboxTestApp.MutateAsync(app, "POST", "/api/feeds/refresh");
        Assert.Equal(200, response.Status);

        var result = InboxTestApp.FromJsonBody(response, InboxJsonContext.Default.RefreshFeedsResponse)!;
        Assert.Equal(1, result.FeedsChecked);
        Assert.Equal(2, result.ItemsAdded);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(0, result.Failed);

        // Items should be in KV
        IKeyValueStore kvStore = kv;
        var index = await kvStore.GetJsonAsync("items:index", InboxJsonContext.Default.StringArray, CancellationToken.None);
        Assert.NotNull(index);
        Assert.Equal(2, index.Length);
    }

    [Fact]
    public async Task RefreshFeeds_skips_duplicate_urls()
    {
        var (app, _, http, _) = InboxTestApp.Create();

        await SeedFeedAsync(app, "https://feed.example.com/rss", http, Rss2Feed);

        // First refresh
        await InboxTestApp.MutateAsync(app, "POST", "/api/feeds/refresh");
        // Second refresh — same feed, same items
        var response = await InboxTestApp.MutateAsync(app, "POST", "/api/feeds/refresh");

        var result = InboxTestApp.FromJsonBody(response, InboxJsonContext.Default.RefreshFeedsResponse)!;
        Assert.Equal(0, result.ItemsAdded);
        Assert.Equal(2, result.Skipped);
    }

    [Fact]
    public async Task RefreshFeeds_increments_failed_on_non_2xx_and_continues_batch()
    {
        var (app, _, http, _) = InboxTestApp.Create();

        // Feed 1: will 404
        http.Respond(r => r.Url == "https://bad.example.com/rss",
            new WasiResponse(404, new Dictionary<string, string>(), []));
        var body1 = InboxTestApp.ToJsonBytes(
            new AddFeedRequest { FeedUrl = "https://bad.example.com/rss" }, InboxJsonContext.Default.AddFeedRequest);
        await InboxTestApp.MutateAsync(app, "POST", "/api/feeds", body1);

        // Feed 2: will succeed
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
        Assert.Equal(2, result.ItemsAdded);  // good feed still processed
    }

    [Fact]
    public async Task RefreshFeeds_handles_atom_feed()
    {
        var (app, _, http, _) = InboxTestApp.Create();

        await SeedFeedAsync(app, "https://atom.example.com/feed", http, AtomFeed);

        var response = await InboxTestApp.MutateAsync(app, "POST", "/api/feeds/refresh");
        var result = InboxTestApp.FromJsonBody(response, InboxJsonContext.Default.RefreshFeedsResponse)!;

        Assert.Equal(1, result.FeedsChecked);
        Assert.Equal(1, result.ItemsAdded);
    }
}
