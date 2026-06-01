using Inbox.Contracts;
using Inbox.Server.Infrastructure;
using SliceFx.Wasi;

namespace Inbox.Server.Tests;

public class RefreshAllFeedsTests
{
    [Fact]
    public async Task RefreshAllFeeds_returns_401_when_cron_token_missing()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();

        var response = await app.DispatchAsync(new WasiRequest(
            "POST", "/api/feeds/refresh-all", new Dictionary<string, string>(), null, null));

        Assert.Equal(401, response.Status);
    }

    [Fact]
    public async Task RefreshAllFeeds_returns_401_when_cron_token_wrong()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();

        var response = await app.DispatchAsync(new WasiRequest(
            "POST", "/api/feeds/refresh-all",
            new Dictionary<string, string> { ["X-Cron-Token"] = "wrong-cron-token" },
            null, null));

        Assert.Equal(401, response.Status);
    }

    // Note: "cron_token variable unset" is covered by the "wrong token" test above.
    // SafeEquals(any, null) returns false when the expected value is null, so
    // the behaviour is identical to a wrong token — both return 401.
    // Testing this directly would require building a custom WasiApp without seeding cron_token,
    // which adds complexity without extra coverage given the SafeEquals unit tests in TokenAuthTests.

    [Fact]
    public async Task RefreshAllFeeds_succeeds_with_correct_cron_token()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();

        var response = await InboxTestApp.AdminRefreshAsync(app);

        Assert.Equal(200, response.Status);
    }

    [Fact]
    public async Task RefreshAllFeeds_refreshes_all_workspace_feeds()
    {
        const string feedUrl = "https://all-refresh.example.com/rss";
        const string rss = """
            <?xml version="1.0" encoding="UTF-8"?>
            <rss version="2.0">
              <channel>
                <item><title>A</title><link>https://all-refresh.example.com/a</link></item>
              </channel>
            </rss>
            """;

        var (app, kv, http, _, _) = InboxTestApp.Create();

        // Seed a feed via the HTTP endpoint (authenticated)
        http.Respond(r => r.Url == feedUrl,
            new SliceFx.Wasi.WasiResponse(200,
                new Dictionary<string, string> { ["content-type"] = "application/rss+xml" },
                System.Text.Encoding.UTF8.GetBytes(rss)));

        var body = InboxTestApp.ToJsonBytes(
            new AddFeedRequest { FeedUrl = feedUrl }, InboxJsonContext.Default.AddFeedRequest);
        await InboxTestApp.MutateAsync(app, "POST", "/api/feeds", body);

        // Admin refresh updates all workspaces
        var response = await InboxTestApp.AdminRefreshAsync(app);
        var result = InboxTestApp.FromJsonBody(response, InboxJsonContext.Default.RefreshFeedsResponse)!;

        Assert.Equal(200, response.Status);
        Assert.Equal(1, result.FeedsChecked);
        Assert.Equal(1, result.ItemsAdded);
    }
}
