using Inbox.Contracts;
using SliceFx.Wasi;

namespace Inbox.Server.Tests;

public class AuthTests
{
    // DELETE and POST /api/feeds/refresh have no required body, so auth fires before any body check.
    [Theory]
    [InlineData("DELETE", "/api/items/abc")]
    [InlineData("POST", "/api/feeds/refresh")]
    public async Task Body_free_mutating_endpoints_return_401_when_token_missing(string method, string path)
    {
        var (app, _, _, _, _) = InboxTestApp.Create();
        var response = await app.DispatchAsync(new WasiRequest(
            method, path, new Dictionary<string, string>(), null, null));

        Assert.Equal(401, response.Status);
    }

    [Theory]
    [InlineData("DELETE", "/api/items/abc")]
    [InlineData("POST", "/api/feeds/refresh")]
    public async Task Body_free_mutating_endpoints_return_401_when_token_wrong(string method, string path)
    {
        var (app, _, _, _, _) = InboxTestApp.Create();
        var response = await app.DispatchAsync(new WasiRequest(
            method, path,
            new Dictionary<string, string> { ["X-Workspace-Token"] = "wrong-token" },
            null, null));

        Assert.Equal(401, response.Status);
    }

    // Body-requiring endpoints: body validation fires before auth (correct API design — inputs validated
    // before processing). Send a valid body so auth can run. Note: wrong token → 401.
    [Fact]
    public async Task PostItem_returns_401_when_token_wrong_with_valid_body()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();
        var body = InboxTestApp.ToJsonBytes(
            new PostItemRequest { Url = "https://example.com" }, InboxJsonContext.Default.PostItemRequest);
        var response = await InboxTestApp.MutateAsync(app, "POST", "/api/items", body, "wrong-token");
        Assert.Equal(401, response.Status);
    }

    [Fact]
    public async Task UpdateItem_returns_401_when_token_wrong_with_valid_body()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();
        var body = InboxTestApp.ToJsonBytes(
            new UpdateItemRequest { Status = ItemStatus.Read }, InboxJsonContext.Default.UpdateItemRequest);
        var response = await InboxTestApp.MutateAsync(app, "PATCH", "/api/items/someId", body, "wrong-token");
        Assert.Equal(401, response.Status);
    }

    [Fact]
    public async Task AddFeed_returns_401_when_token_wrong_with_valid_body()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();
        var body = InboxTestApp.ToJsonBytes(
            new AddFeedRequest { FeedUrl = "https://example.com/feed" }, InboxJsonContext.Default.AddFeedRequest);
        var response = await InboxTestApp.MutateAsync(app, "POST", "/api/feeds", body, "wrong-token");
        Assert.Equal(401, response.Status);
    }

    // GET endpoints now require a valid workspace token (public-read leak fixed).
    [Theory]
    [InlineData("GET", "/api/items")]
    [InlineData("GET", "/api/items/abc")]
    [InlineData("GET", "/api/feeds")]
    public async Task GET_endpoints_return_401_when_token_missing(string method, string path)
    {
        var (app, _, _, _, _) = InboxTestApp.Create();
        var response = await app.DispatchAsync(new WasiRequest(
            method, path, new Dictionary<string, string>(), null, null));

        Assert.Equal(401, response.Status);
    }

    [Theory]
    [InlineData("GET", "/api/items")]
    [InlineData("GET", "/api/items/abc")]
    [InlineData("GET", "/api/feeds")]
    public async Task GET_endpoints_return_401_when_token_wrong(string method, string path)
    {
        var (app, _, _, _, _) = InboxTestApp.Create();
        var response = await app.DispatchAsync(new WasiRequest(
            method, path,
            new Dictionary<string, string> { ["X-Workspace-Token"] = "wrong-token" },
            null, null));

        Assert.Equal(401, response.Status);
    }
}
