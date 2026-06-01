using Inbox.Contracts;
using SliceFx.Wasi;

namespace Inbox.Server.Tests;

/// <summary>
/// Comprehensive authentication tests: all endpoints, all failure modes.
/// Covers the public-read leak fix: GETs now require a valid workspace token.
/// </summary>
public class AuthenticationTests
{
    private static async Task<string> SeedItemIdAsync(SliceFx.Wasi.WasiApp app)
    {
        var body = InboxTestApp.ToJsonBytes(
            new PostItemRequest { Url = "https://example.com" }, InboxJsonContext.Default.PostItemRequest);
        var resp = InboxTestApp.FromJsonBody(
            await InboxTestApp.MutateAsync(app, "POST", "/api/items", body),
            InboxJsonContext.Default.PostItemResponse)!;
        return resp.Id;
    }

    [Theory]
    [InlineData("GET", "/api/items")]
    [InlineData("GET", "/api/feeds")]
    [InlineData("POST", "/api/feeds/refresh")]
    [InlineData("DELETE", "/api/items/any-id")]
    public async Task Endpoints_return_401_when_no_token_sent(string method, string path)
    {
        var (app, _, _, _, _) = InboxTestApp.Create();

        var response = await app.DispatchAsync(new WasiRequest(
            method, path, new Dictionary<string, string>(), null, null));

        Assert.Equal(401, response.Status);
    }

    [Theory]
    [InlineData("GET", "/api/items")]
    [InlineData("GET", "/api/feeds")]
    [InlineData("POST", "/api/feeds/refresh")]
    [InlineData("DELETE", "/api/items/any-id")]
    public async Task Endpoints_return_401_when_invalid_token_sent(string method, string path)
    {
        var (app, _, _, _, _) = InboxTestApp.Create();

        var response = await app.DispatchAsync(new WasiRequest(
            method, path,
            new Dictionary<string, string> { ["X-Workspace-Token"] = "not-a-valid-token" },
            null, null));

        Assert.Equal(401, response.Status);
    }

    [Fact]
    public async Task GetItems_returns_200_with_valid_token()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();
        var response = await InboxTestApp.GetAsync(app, "/api/items");
        Assert.Equal(200, response.Status);
    }

    [Fact]
    public async Task GetItem_returns_401_without_token()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();
        var id = await SeedItemIdAsync(app);

        var response = await app.DispatchAsync(new WasiRequest(
            "GET", $"/api/items/{id}", new Dictionary<string, string>(), null, null));
        Assert.Equal(401, response.Status);
    }

    [Fact]
    public async Task GetItem_returns_200_with_valid_token()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();
        var id = await SeedItemIdAsync(app);

        var response = await InboxTestApp.GetAsync(app, $"/api/items/{id}");
        Assert.Equal(200, response.Status);
    }

    [Fact]
    public async Task GetFeeds_returns_200_with_valid_token()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();
        var response = await InboxTestApp.GetAsync(app, "/api/feeds");
        Assert.Equal(200, response.Status);
    }

    [Fact]
    public async Task Unauthenticated_endpoints_are_reachable_without_token()
    {
        // POST /api/workspaces and POST /api/demo require no token
        var (app, _, _, _, _) = InboxTestApp.Create();

        var wsResp = await app.DispatchAsync(new WasiRequest(
            "POST", "/api/workspaces", new Dictionary<string, string>(), null, null));
        Assert.NotEqual(401, wsResp.Status);

        var demoResp = await app.DispatchAsync(new WasiRequest(
            "POST", "/api/demo", new Dictionary<string, string>(), null, null));
        Assert.NotEqual(401, demoResp.Status);
    }
}
