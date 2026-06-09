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
        var (app, _, _, _, _) = InboxTestApp.Create();
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
        var (app, _, _, _, _) = InboxTestApp.Create();
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
        var (app, _, _, _, _) = InboxTestApp.Create();
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
        var (app, _, _, _, _) = InboxTestApp.Create();
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
        var (app, _, _, _, _) = InboxTestApp.Create();
        await SeedItemAsync(app, "https://a.com", status: ItemStatus.Read);
        await SeedItemAsync(app, "https://b.com");

        var response = await InboxTestApp.GetAsync(app, "/api/items", "?status=");
        var result = InboxTestApp.FromJsonBody(response, InboxJsonContext.Default.GetItemsResponse)!;

        Assert.Equal(2, result.Total);
    }

    // ── Pagination ──

    [Fact]
    public async Task GetItems_returns_newest_first_by_default()
    {
        // Seeded at different times (sequential awaits guarantee ordering).
        var (app, _, _, _, _) = InboxTestApp.Create();
        await SeedItemAsync(app, "https://older.com");
        await Task.Delay(10); // ensure SavedAt differs
        await SeedItemAsync(app, "https://newer.com");

        var response = await InboxTestApp.GetAsync(app, "/api/items");
        Assert.Equal(200, response.Status);

        var result = InboxTestApp.FromJsonBody(response, InboxJsonContext.Default.GetItemsResponse)!;
        Assert.Equal(2, result.Total);
        // Newest item must appear first (descending SavedAt)
        Assert.Equal("https://newer.com", result.Items[0].Url);
        Assert.Equal("https://older.com", result.Items[1].Url);
    }

    [Fact]
    public async Task GetItems_limit_returns_first_page_and_total()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();
        await SeedItemAsync(app, "https://a.com");
        await SeedItemAsync(app, "https://b.com");
        await SeedItemAsync(app, "https://c.com");

        var response = await InboxTestApp.GetAsync(app, "/api/items", "?limit=2&offset=0");
        Assert.Equal(200, response.Status);

        var result = InboxTestApp.FromJsonBody(response, InboxJsonContext.Default.GetItemsResponse)!;
        // Total = all matched items (3), Items.Length = page size (2)
        Assert.Equal(3, result.Total);
        Assert.Equal(2, result.Items.Length);
    }

    [Fact]
    public async Task GetItems_offset_returns_second_page()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();
        await SeedItemAsync(app, "https://a.com");
        await SeedItemAsync(app, "https://b.com");
        await SeedItemAsync(app, "https://c.com");

        var page1 = await InboxTestApp.GetAsync(app, "/api/items", "?limit=2&offset=0");
        var page2 = await InboxTestApp.GetAsync(app, "/api/items", "?limit=2&offset=2");

        var r1 = InboxTestApp.FromJsonBody(page1, InboxJsonContext.Default.GetItemsResponse)!;
        var r2 = InboxTestApp.FromJsonBody(page2, InboxJsonContext.Default.GetItemsResponse)!;

        Assert.Equal(3, r1.Total);
        Assert.Equal(2, r1.Items.Length);

        Assert.Equal(3, r2.Total);
        Assert.Single(r2.Items); // one item remaining on second page

        // Page 1 and page 2 together cover all unique IDs
        var allIds = r1.Items.Select(i => i.Id).Concat(r2.Items.Select(i => i.Id)).ToHashSet();
        Assert.Equal(3, allIds.Count);
    }

    [Fact]
    public async Task GetItems_offset_beyond_total_returns_empty_items_with_correct_total()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();
        await SeedItemAsync(app, "https://a.com");

        var response = await InboxTestApp.GetAsync(app, "/api/items", "?limit=10&offset=100");
        Assert.Equal(200, response.Status);

        var result = InboxTestApp.FromJsonBody(response, InboxJsonContext.Default.GetItemsResponse)!;
        Assert.Equal(1, result.Total); // total unchanged
        Assert.Empty(result.Items);   // no items on this page
    }

    [Fact]
    public async Task GetItems_limit_larger_than_total_returns_all_items()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();
        await SeedItemAsync(app, "https://a.com");
        await SeedItemAsync(app, "https://b.com");

        var response = await InboxTestApp.GetAsync(app, "/api/items", "?limit=100&offset=0");
        Assert.Equal(200, response.Status);

        var result = InboxTestApp.FromJsonBody(response, InboxJsonContext.Default.GetItemsResponse)!;
        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.Items.Length);
    }
}
