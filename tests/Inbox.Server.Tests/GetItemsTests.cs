using Inbox.Contracts;

namespace Inbox.Server.Tests;

public class GetItemsTests
{
    private static async Task SeedItemAsync(
        SliceFx.Wasi.WasiApp app, string url, string? tag = null, string status = ItemStatus.Unread)
    {
        var reqBody = InboxTestApp.ToJsonBytes(
            new PostItemRequest { Url = url }, InboxJsonContext.Default.PostItemRequest);
        var postResp = await InboxTestApp.MutateAsync(app, "POST", "/api/items", reqBody);
        Assert.Equal(200, postResp.Status);

        if (tag is not null || status != ItemStatus.Unread)
        {
            var posted = InboxTestApp.FromJsonBody(postResp, InboxJsonContext.Default.PostItemResponse)!;
            var patchBody = InboxTestApp.ToJsonBytes(
                new UpdateItemRequest
                {
                    Status = status != ItemStatus.Unread ? status : null,
                    Tags = tag is not null ? [tag] : null,
                },
                InboxJsonContext.Default.UpdateItemRequest);
            await InboxTestApp.MutateAsync(app, "PATCH", $"/api/items/{posted.Id}", patchBody);
        }
    }

    [Fact]
    public async Task GetItems_returns_all_items_without_filters()
    {
        var (app, _, _, _) = InboxTestApp.Create();
        await SeedItemAsync(app, "https://a.com");
        await SeedItemAsync(app, "https://b.com");

        var response = await InboxTestApp.GetAsync(app, "/api/items");
        Assert.Equal(200, response.Status);

        var result = InboxTestApp.FromJsonBody(response, InboxJsonContext.Default.GetItemsResponse)!;
        Assert.Equal(2, result.Total);
    }

    [Fact]
    public async Task GetItems_filters_by_q_on_title_and_url()
    {
        var (app, _, _, _) = InboxTestApp.Create();
        await SeedItemAsync(app, "https://example.com/article");
        await SeedItemAsync(app, "https://other.com/page");

        var response = await InboxTestApp.GetAsync(app, "/api/items", "?q=example");
        var result = InboxTestApp.FromJsonBody(response, InboxJsonContext.Default.GetItemsResponse)!;

        Assert.Equal(1, result.Total);
        Assert.Equal("https://example.com/article", result.Items[0].Url);
    }

    [Fact]
    public async Task GetItems_filters_by_tag()
    {
        var (app, _, _, _) = InboxTestApp.Create();
        await SeedItemAsync(app, "https://a.com", tag: "tech");
        await SeedItemAsync(app, "https://b.com", tag: "news");

        var response = await InboxTestApp.GetAsync(app, "/api/items", "?tag=tech");
        var result = InboxTestApp.FromJsonBody(response, InboxJsonContext.Default.GetItemsResponse)!;

        Assert.Equal(1, result.Total);
        Assert.Equal("https://a.com", result.Items[0].Url);
    }

    [Fact]
    public async Task GetItems_filters_by_status()
    {
        var (app, _, _, _) = InboxTestApp.Create();
        await SeedItemAsync(app, "https://a.com", status: ItemStatus.Read);
        await SeedItemAsync(app, "https://b.com");

        var response = await InboxTestApp.GetAsync(app, "/api/items", "?status=read");
        var result = InboxTestApp.FromJsonBody(response, InboxJsonContext.Default.GetItemsResponse)!;

        Assert.Equal(1, result.Total);
        Assert.Equal("https://a.com", result.Items[0].Url);
    }

    [Fact]
    public async Task GetItems_with_empty_status_returns_all_items()
    {
        // Characterization test: empty ?status= is treated as "no filter" (IsNullOrEmpty semantics).
        // This is correct intended behaviour — the WASI binder binds empty string? as Bound(""),
        // and GetItems uses IsNullOrEmpty to treat "" the same as absent.
        var (app, _, _, _) = InboxTestApp.Create();
        await SeedItemAsync(app, "https://a.com", status: ItemStatus.Read);
        await SeedItemAsync(app, "https://b.com");

        var response = await InboxTestApp.GetAsync(app, "/api/items", "?status=");
        var result = InboxTestApp.FromJsonBody(response, InboxJsonContext.Default.GetItemsResponse)!;

        Assert.Equal(2, result.Total);
    }
}
