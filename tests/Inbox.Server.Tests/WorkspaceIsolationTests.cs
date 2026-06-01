using Inbox.Contracts;

namespace Inbox.Server.Tests;

/// <summary>
/// Verifies that workspaces are properly isolated from each other.
/// This is the primary regression test for the public-read information-leak fix.
/// </summary>
public class WorkspaceIsolationTests
{
    private const string TokenA = "token-workspace-a";
    private const string TokenB = "token-workspace-b";
    private const string WidA = "wid-a";
    private const string WidB = "wid-b";

    [Fact]
    public async Task Items_posted_to_workspace_A_are_not_visible_from_workspace_B()
    {
        var (app, kv, _, _, _) = InboxTestApp.Create();

        // Seed two additional workspaces
        await InboxTestApp.SeedWorkspaceAsync(kv, TokenA, WidA);
        await InboxTestApp.SeedWorkspaceAsync(kv, TokenB, WidB);

        // Post an item to workspace A
        var body = InboxTestApp.ToJsonBytes(
            new PostItemRequest { Url = "https://workspace-a-secret.example.com" },
            InboxJsonContext.Default.PostItemRequest);
        var postResp = await InboxTestApp.MutateAsync(app, "POST", "/api/items", body, TokenA);
        Assert.Equal(200, postResp.Status);

        // Get items from workspace B — must not see workspace A's item
        var bResponse = await InboxTestApp.GetAsync(app, "/api/items", token: TokenB);
        Assert.Equal(200, bResponse.Status);
        var bResult = InboxTestApp.FromJsonBody(bResponse, InboxJsonContext.Default.GetItemsResponse)!;
        Assert.Equal(0, bResult.Total);
        Assert.Empty(bResult.Items);
    }

    [Fact]
    public async Task Items_are_isolated_by_workspace_GetItem()
    {
        var (app, kv, _, _, _) = InboxTestApp.Create();

        await InboxTestApp.SeedWorkspaceAsync(kv, TokenA, WidA);
        await InboxTestApp.SeedWorkspaceAsync(kv, TokenB, WidB);

        // Post an item to workspace A, capture its ID
        var body = InboxTestApp.ToJsonBytes(
            new PostItemRequest { Url = "https://private.example.com/a" },
            InboxJsonContext.Default.PostItemRequest);
        var postResp = await InboxTestApp.MutateAsync(app, "POST", "/api/items", body, TokenA);
        var posted = InboxTestApp.FromJsonBody(postResp, InboxJsonContext.Default.PostItemResponse)!;

        // Workspace B cannot access that specific item
        var bResponse = await InboxTestApp.GetAsync(app, $"/api/items/{posted.Id}", token: TokenB);
        Assert.Equal(404, bResponse.Status);
    }

    [Fact]
    public async Task Feeds_are_isolated_by_workspace()
    {
        var (app, kv, _, _, _) = InboxTestApp.Create();

        await InboxTestApp.SeedWorkspaceAsync(kv, TokenA, WidA);
        await InboxTestApp.SeedWorkspaceAsync(kv, TokenB, WidB);

        // Add a feed to workspace A
        var body = InboxTestApp.ToJsonBytes(
            new AddFeedRequest { FeedUrl = "https://feed-a.example.com/rss" },
            InboxJsonContext.Default.AddFeedRequest);
        var feedResp = await InboxTestApp.MutateAsync(app, "POST", "/api/feeds", body, TokenA);
        Assert.Equal(200, feedResp.Status);

        // Workspace B sees no feeds
        var bFeeds = await InboxTestApp.GetAsync(app, "/api/feeds", token: TokenB);
        Assert.Equal(200, bFeeds.Status);
        var bResult = InboxTestApp.FromJsonBody(bFeeds, InboxJsonContext.Default.GetFeedsResponse)!;
        Assert.Equal(0, bResult.Total);
    }

    [Fact]
    public async Task Delete_in_workspace_A_does_not_affect_workspace_B()
    {
        var (app, kv, _, _, _) = InboxTestApp.Create();

        await InboxTestApp.SeedWorkspaceAsync(kv, TokenA, WidA);
        await InboxTestApp.SeedWorkspaceAsync(kv, TokenB, WidB);

        // Both workspaces add the same URL (same content, different owners)
        var bodyA = InboxTestApp.ToJsonBytes(
            new PostItemRequest { Url = "https://shared-article.example.com" },
            InboxJsonContext.Default.PostItemRequest);
        var bodyB = InboxTestApp.ToJsonBytes(
            new PostItemRequest { Url = "https://shared-article.example.com" },
            InboxJsonContext.Default.PostItemRequest);

        var aResp = InboxTestApp.FromJsonBody(
            await InboxTestApp.MutateAsync(app, "POST", "/api/items", bodyA, TokenA),
            InboxJsonContext.Default.PostItemResponse)!;
        var bResp = InboxTestApp.FromJsonBody(
            await InboxTestApp.MutateAsync(app, "POST", "/api/items", bodyB, TokenB),
            InboxJsonContext.Default.PostItemResponse)!;

        // Workspace A deletes its item
        var deleteResp = await InboxTestApp.MutateAsync(app, "DELETE", $"/api/items/{aResp.Id}", token: TokenA);
        Assert.Equal(204, deleteResp.Status);

        // Workspace B's item must still be there
        var bItem = await InboxTestApp.GetAsync(app, $"/api/items/{bResp.Id}", token: TokenB);
        Assert.Equal(200, bItem.Status);

        var bItemResult = InboxTestApp.FromJsonBody(bItem, InboxJsonContext.Default.GetItemResponse)!;
        Assert.Equal(bResp.Id, bItemResult.Id);
    }
}
