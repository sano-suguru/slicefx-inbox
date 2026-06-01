using Inbox.Contracts;

namespace Inbox.Server.Tests;

public class FeedTests
{
    private static async Task AddFeedAsync(SliceFx.Wasi.WasiApp app, string feedUrl)
    {
        var body = InboxTestApp.ToJsonBytes(
            new AddFeedRequest { FeedUrl = feedUrl }, InboxJsonContext.Default.AddFeedRequest);
        await InboxTestApp.MutateAsync(app, "POST", "/api/feeds", body);
    }

    [Fact]
    public async Task AddFeed_saves_feed_and_returns_response()
    {
        const string feedUrl = "https://example.com/rss";
        var (app, _, _, _) = InboxTestApp.Create();

        var body = InboxTestApp.ToJsonBytes(
            new AddFeedRequest { FeedUrl = feedUrl }, InboxJsonContext.Default.AddFeedRequest);
        var response = await InboxTestApp.MutateAsync(app, "POST", "/api/feeds", body);

        Assert.Equal(200, response.Status);
        var result = InboxTestApp.FromJsonBody(response, InboxJsonContext.Default.AddFeedResponse)!;
        Assert.False(string.IsNullOrEmpty(result.Id));
        Assert.Equal(feedUrl, result.FeedUrl);
    }

    [Fact]
    public async Task AddFeed_adds_to_existing_feeds_index()
    {
        // AddFeed has no dedup — use distinct URLs to avoid asserting a deduplication that doesn't exist.
        var (app, _, _, _) = InboxTestApp.Create();

        await AddFeedAsync(app, "https://feed1.example.com/rss");
        await AddFeedAsync(app, "https://feed2.example.com/rss");

        var response = await InboxTestApp.GetAsync(app, "/api/feeds");
        var result = InboxTestApp.FromJsonBody(response, InboxJsonContext.Default.GetFeedsResponse)!;
        Assert.Equal(2, result.Total);
    }

    [Fact]
    public async Task AddFeed_returns_400_for_invalid_url()
    {
        var (app, _, _, _) = InboxTestApp.Create();

        var body = InboxTestApp.ToJsonBytes(
            new AddFeedRequest { FeedUrl = "not-a-url" }, InboxJsonContext.Default.AddFeedRequest);
        var response = await InboxTestApp.MutateAsync(app, "POST", "/api/feeds", body);

        Assert.Equal(400, response.Status);
    }

    [Fact]
    public async Task GetFeeds_returns_empty_list_when_no_feeds()
    {
        var (app, _, _, _) = InboxTestApp.Create();

        var response = await InboxTestApp.GetAsync(app, "/api/feeds");

        Assert.Equal(200, response.Status);
        var result = InboxTestApp.FromJsonBody(response, InboxJsonContext.Default.GetFeedsResponse)!;
        Assert.Empty(result.Feeds);
        Assert.Equal(0, result.Total);
    }

    [Fact]
    public async Task GetFeeds_returns_all_subscribed_feeds()
    {
        const string url1 = "https://feed1.example.com/rss";
        const string url2 = "https://feed2.example.com/rss";
        var (app, _, _, _) = InboxTestApp.Create();

        await AddFeedAsync(app, url1);
        await AddFeedAsync(app, url2);

        var response = await InboxTestApp.GetAsync(app, "/api/feeds");
        var result = InboxTestApp.FromJsonBody(response, InboxJsonContext.Default.GetFeedsResponse)!;

        Assert.Equal(2, result.Total);
        var feedUrls = result.Feeds.Select(f => f.FeedUrl).ToArray();
        Assert.Contains(url1, feedUrls);
        Assert.Contains(url2, feedUrls);
    }
}
