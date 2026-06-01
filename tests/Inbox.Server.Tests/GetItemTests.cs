using Inbox.Contracts;

namespace Inbox.Server.Tests;

public class GetItemTests
{
    private static async Task<string> CreateItemAsync(SliceFx.Wasi.WasiApp app, string url = "https://example.com")
    {
        var body = InboxTestApp.ToJsonBytes(
            new PostItemRequest { Url = url }, InboxJsonContext.Default.PostItemRequest);
        var resp = InboxTestApp.FromJsonBody(
            await InboxTestApp.MutateAsync(app, "POST", "/api/items", body),
            InboxJsonContext.Default.PostItemResponse)!;
        return resp.Id;
    }

    [Fact]
    public async Task GetItem_returns_item_when_found()
    {
        const string url = "https://example.com/article";
        var (app, _, _, _, _) = InboxTestApp.Create();

        var id = await CreateItemAsync(app, url);

        var response = await InboxTestApp.GetAsync(app, $"/api/items/{id}");

        Assert.Equal(200, response.Status);
        var item = InboxTestApp.FromJsonBody(response, InboxJsonContext.Default.GetItemResponse)!;
        Assert.Equal(id, item.Id);
        Assert.Equal(url, item.Url);
        // InMemoryWasiHttpClient returns 200 + empty body + no content-type when no stub matches,
        // so the og:title branch is skipped and Title stays the posted URL (PostItem.cs).
        Assert.Equal(url, item.Title);
        Assert.Equal(ItemStatus.Unread, item.Status);
        Assert.Equal("bookmark", item.Source);
        Assert.Null(item.Description);
        Assert.Null(item.Tags);
    }

    [Fact]
    public async Task GetItem_returns_404_when_not_found()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();

        var response = await InboxTestApp.GetAsync(app, "/api/items/nonexistent");

        Assert.Equal(404, response.Status);
    }
}
