using System.Text;
using Inbox.Contracts;

namespace Inbox.Server.Tests;

/// <summary>
/// Regression tests for URL normalization in feed deduplication (Fix 5).
/// </summary>
public class DedupNormalizationTests
{
    private static string MakeRss(params string[] links)
    {
        var items = string.Concat(links.Select((l, i) => $"<item><title>T{i}</title><link>{l}</link></item>"));
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <rss version="2.0"><channel>{items}</channel></rss>
            """;
    }

    [Fact]
    public async Task RefreshFeeds_deduplicates_trailing_slash_variants()
    {
        var (app, _, http, _, _) = InboxTestApp.Create();

        // First refresh: add an item with a trailing slash
        var rss1 = MakeRss("https://example.com/article/");
        http.Respond(r => r.Url == "https://example.com/rss",
            new SliceFx.Wasi.WasiResponse(200,
                new Dictionary<string, string> { ["content-type"] = "application/rss+xml" },
                Encoding.UTF8.GetBytes(rss1)));
        var feedBody = InboxTestApp.ToJsonBytes(
            new AddFeedRequest { FeedUrl = "https://example.com/rss" }, InboxJsonContext.Default.AddFeedRequest);
        await InboxTestApp.MutateAsync(app, "POST", "/api/feeds", feedBody);
        var r1 = InboxTestApp.FromJsonBody(
            await InboxTestApp.MutateAsync(app, "POST", "/api/feeds/refresh"),
            InboxJsonContext.Default.RefreshFeedsResponse)!;
        Assert.Equal(1, r1.ItemsAdded);

        // Second refresh: same article without trailing slash — must be deduped
        var rss2 = MakeRss("https://example.com/article");
        http.Respond(r => r.Url == "https://example.com/rss",
            new SliceFx.Wasi.WasiResponse(200,
                new Dictionary<string, string> { ["content-type"] = "application/rss+xml" },
                Encoding.UTF8.GetBytes(rss2)));
        var r2 = InboxTestApp.FromJsonBody(
            await InboxTestApp.MutateAsync(app, "POST", "/api/feeds/refresh"),
            InboxJsonContext.Default.RefreshFeedsResponse)!;
        Assert.Equal(0, r2.ItemsAdded);
        Assert.Equal(1, r2.Skipped);
    }

    [Fact]
    public async Task RefreshFeeds_deduplicates_host_casing_variants()
    {
        var (app, _, http, _, _) = InboxTestApp.Create();

        var rss1 = MakeRss("https://Example.Com/article");
        http.Respond(r => r.Url == "https://example.com/rss",
            new SliceFx.Wasi.WasiResponse(200,
                new Dictionary<string, string> { ["content-type"] = "application/rss+xml" },
                Encoding.UTF8.GetBytes(rss1)));
        var feedBody = InboxTestApp.ToJsonBytes(
            new AddFeedRequest { FeedUrl = "https://example.com/rss" }, InboxJsonContext.Default.AddFeedRequest);
        await InboxTestApp.MutateAsync(app, "POST", "/api/feeds", feedBody);
        var r1 = InboxTestApp.FromJsonBody(
            await InboxTestApp.MutateAsync(app, "POST", "/api/feeds/refresh"),
            InboxJsonContext.Default.RefreshFeedsResponse)!;
        Assert.Equal(1, r1.ItemsAdded);

        // Same URL with lowercase host — must be deduped
        var rss2 = MakeRss("https://example.com/article");
        http.Respond(r => r.Url == "https://example.com/rss",
            new SliceFx.Wasi.WasiResponse(200,
                new Dictionary<string, string> { ["content-type"] = "application/rss+xml" },
                Encoding.UTF8.GetBytes(rss2)));
        var r2 = InboxTestApp.FromJsonBody(
            await InboxTestApp.MutateAsync(app, "POST", "/api/feeds/refresh"),
            InboxJsonContext.Default.RefreshFeedsResponse)!;
        Assert.Equal(0, r2.ItemsAdded);
        Assert.Equal(1, r2.Skipped);
    }

    [Fact]
    public async Task RefreshFeeds_does_not_dedup_different_path_casing()
    {
        // Path casing IS significant (servers can distinguish /Article vs /article).
        // Normalization must NOT collapse path-case differences.
        var (app, _, http, _, _) = InboxTestApp.Create();

        var rss = MakeRss("https://example.com/Article", "https://example.com/article");
        http.Respond(r => r.Url == "https://example.com/rss",
            new SliceFx.Wasi.WasiResponse(200,
                new Dictionary<string, string> { ["content-type"] = "application/rss+xml" },
                Encoding.UTF8.GetBytes(rss)));
        var feedBody = InboxTestApp.ToJsonBytes(
            new AddFeedRequest { FeedUrl = "https://example.com/rss" }, InboxJsonContext.Default.AddFeedRequest);
        await InboxTestApp.MutateAsync(app, "POST", "/api/feeds", feedBody);
        var result = InboxTestApp.FromJsonBody(
            await InboxTestApp.MutateAsync(app, "POST", "/api/feeds/refresh"),
            InboxJsonContext.Default.RefreshFeedsResponse)!;

        // Both paths are distinct → both should be ingested
        Assert.Equal(2, result.ItemsAdded);
    }
}
