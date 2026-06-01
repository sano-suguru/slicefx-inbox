using System.Collections.Generic;
using System.Text;
using Inbox.Contracts;
using Inbox.Server.Infrastructure;
using SliceFx.Wasi;
using SliceFx.Wasi.HttpClient;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Tests;

public class PostItemTests
{
    [Fact]
    public async Task PostItem_sets_title_from_og_title()
    {
        var (app, kv, http, _, _) = InboxTestApp.Create();

        const string html = """
            <html>
              <head>
                <meta property="og:title" content="Extracted OG Title">
                <meta name="description" content="Extracted description.">
              </head>
            </html>
            """;

        http.Respond(
            r => r.Url == "https://example.com/article",
            new WasiResponse(200,
                new Dictionary<string, string> { ["content-type"] = "text/html; charset=utf-8" },
                Encoding.UTF8.GetBytes(html)));

        var reqBody = InboxTestApp.ToJsonBytes(
            new PostItemRequest { Url = "https://example.com/article" },
            InboxJsonContext.Default.PostItemRequest);
        var response = await InboxTestApp.MutateAsync(app, "POST", "/api/items", reqBody);

        Assert.Equal(200, response.Status);

        var result = InboxTestApp.FromJsonBody(response, InboxJsonContext.Default.PostItemResponse);
        Assert.NotNull(result);
        Assert.NotEmpty(result.Id);
        Assert.Equal("https://example.com/article", result.Url);
        Assert.Equal("Extracted OG Title", result.Title);
        Assert.Equal("Extracted description.", result.Description);

        // KV item must reflect the extracted title and description
        IKeyValueStore kvStore = kv;
        var stored = await kvStore.GetJsonAsync(
            WorkspaceKeys.Item(InboxTestApp.DefaultWid, result.Id), InboxJsonContext.Default.InboxItem,
            CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal("Extracted OG Title", stored.Title);
        Assert.Equal("Extracted description.", stored.Description);
        Assert.Equal(ItemStatus.Unread, stored.Status);
        Assert.Equal("bookmark", stored.Source);

        // Item must appear in the listing (via KvScan prefix scan, not index key).
        var items = await KvScan.ListItemsAsync(kvStore, InboxTestApp.DefaultWid, CancellationToken.None);
        Assert.Contains(items, i => i.Id == result.Id);
    }

    [Fact]
    public async Task PostItem_falls_back_to_url_when_fetch_returns_no_content_type()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();

        var reqBody = InboxTestApp.ToJsonBytes(
            new PostItemRequest { Url = "https://example.com/article" },
            InboxJsonContext.Default.PostItemRequest);
        var response = await InboxTestApp.MutateAsync(app, "POST", "/api/items", reqBody);

        Assert.Equal(200, response.Status);

        var result = InboxTestApp.FromJsonBody(response, InboxJsonContext.Default.PostItemResponse);
        Assert.NotNull(result);
        Assert.Equal("https://example.com/article", result.Title);
        Assert.Null(result.Description);
    }

    [Fact]
    public async Task PostItem_falls_back_to_url_on_non_2xx_response()
    {
        var (app, _, http, _, _) = InboxTestApp.Create();

        http.Respond(
            r => r.Url == "https://example.com/article",
            new WasiResponse(404, new Dictionary<string, string>(), []));

        var reqBody = InboxTestApp.ToJsonBytes(
            new PostItemRequest { Url = "https://example.com/article" },
            InboxJsonContext.Default.PostItemRequest);
        var response = await InboxTestApp.MutateAsync(app, "POST", "/api/items", reqBody);

        Assert.Equal(200, response.Status);

        var result = InboxTestApp.FromJsonBody(response, InboxJsonContext.Default.PostItemResponse);
        Assert.NotNull(result);
        Assert.Equal("https://example.com/article", result.Title);
        Assert.Null(result.Description);
    }

    [Fact]
    public async Task PostItem_follows_https_redirect_to_extract_title()
    {
        var (app, _, http, _, _) = InboxTestApp.Create();

        const string html = """
            <html><head><title>Real Page Title</title></head></html>
            """;

        http.Respond(
            r => r.Url == "https://short.url/abc",
            new WasiResponse(301,
                new Dictionary<string, string> { ["location"] = "https://real.example.com/article" },
                []));
        http.Respond(
            r => r.Url == "https://real.example.com/article",
            new WasiResponse(200,
                new Dictionary<string, string> { ["content-type"] = "text/html; charset=utf-8" },
                Encoding.UTF8.GetBytes(html)));

        var reqBody = InboxTestApp.ToJsonBytes(
            new PostItemRequest { Url = "https://short.url/abc" },
            InboxJsonContext.Default.PostItemRequest);
        var response = await InboxTestApp.MutateAsync(app, "POST", "/api/items", reqBody);

        var result = InboxTestApp.FromJsonBody(response, InboxJsonContext.Default.PostItemResponse);
        Assert.NotNull(result);
        Assert.Equal("https://short.url/abc", result.Url);
        Assert.Equal("Real Page Title", result.Title);
    }

    [Fact]
    public async Task PostItem_falls_back_to_url_on_http_redirect_location()
    {
        var (app, _, http, _, _) = InboxTestApp.Create();

        http.Respond(
            r => r.Url == "https://example.com/article",
            new WasiResponse(301,
                new Dictionary<string, string> { ["location"] = "http://insecure.example.com/page" },
                []));

        var reqBody = InboxTestApp.ToJsonBytes(
            new PostItemRequest { Url = "https://example.com/article" },
            InboxJsonContext.Default.PostItemRequest);
        var response = await InboxTestApp.MutateAsync(app, "POST", "/api/items", reqBody);

        var result = InboxTestApp.FromJsonBody(response, InboxJsonContext.Default.PostItemResponse);
        Assert.NotNull(result);
        Assert.Equal("https://example.com/article", result.Title);
    }

    /// <summary>
    /// Characterization test for listing order after the index-to-prefix-scan migration.
    /// Items are sorted by SavedAt ascending, then by Id (Ordinal) as a stable tiebreaker.
    /// Two posted items should both appear in the listing in SavedAt order.
    /// </summary>
    [Fact]
    public async Task PostItem_both_items_appear_in_listing_via_prefix_scan()
    {
        var (app, kv, _, _, _) = InboxTestApp.Create();

        var req1 = InboxTestApp.ToJsonBytes(new PostItemRequest { Url = "https://a.com" }, InboxJsonContext.Default.PostItemRequest);
        var req2 = InboxTestApp.ToJsonBytes(new PostItemRequest { Url = "https://b.com" }, InboxJsonContext.Default.PostItemRequest);

        var r1 = InboxTestApp.FromJsonBody(
            await InboxTestApp.MutateAsync(app, "POST", "/api/items", req1),
            InboxJsonContext.Default.PostItemResponse)!;
        var r2 = InboxTestApp.FromJsonBody(
            await InboxTestApp.MutateAsync(app, "POST", "/api/items", req2),
            InboxJsonContext.Default.PostItemResponse)!;

        // Verify both items are retrievable via the listing endpoint.
        var listResp = await InboxTestApp.GetAsync(app, "/api/items");
        Assert.Equal(200, listResp.Status);
        var listing = InboxTestApp.FromJsonBody(listResp, InboxJsonContext.Default.GetItemsResponse)!;
        Assert.Equal(2, listing.Total);
        Assert.Contains(listing.Items, i => i.Id == r1.Id && i.Url == "https://a.com");
        Assert.Contains(listing.Items, i => i.Id == r2.Id && i.Url == "https://b.com");

        // Verify sorted order: SavedAt ascending; tie-broken by Id (Ordinal).
        // Since the items are posted sequentially, r1.SavedAt <= r2.SavedAt.
        // If timestamps tie (coarse clock), Id order applies.
        IKeyValueStore kvStore = kv;
        var items = await KvScan.ListItemsAsync(kvStore, InboxTestApp.DefaultWid, CancellationToken.None);
        Assert.Equal(2, items.Length);
        for (var i = 0; i < items.Length - 1; i++)
        {
            var cmp = items[i].SavedAt.CompareTo(items[i + 1].SavedAt);
            if (cmp == 0)
                Assert.True(string.Compare(items[i].Id, items[i + 1].Id, StringComparison.Ordinal) <= 0,
                    "Tie in SavedAt must be broken by Id ascending");
            else
                Assert.True(cmp < 0, "Items must be in SavedAt ascending order");
        }
    }
}
