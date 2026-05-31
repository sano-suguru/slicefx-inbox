using System.Collections.Generic;
using System.Text;
using Inbox.Contracts;
using SliceFx.Wasi;
using SliceFx.Wasi.HttpClient;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Tests;

public class PostItemTests
{
    [Fact]
    public async Task PostItem_sets_title_from_og_title()
    {
        var (app, kv, http, _) = InboxTestApp.Create();

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
        // og:title extracted from the fetched page
        Assert.Equal("Extracted OG Title", result.Title);
        Assert.Equal("Extracted description.", result.Description);

        // KV item must reflect the extracted title and description
        IKeyValueStore kvStore = kv;
        var stored = await kvStore.GetJsonAsync($"item:{result.Id}", InboxJsonContext.Default.InboxItem,
            CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal("Extracted OG Title", stored.Title);
        Assert.Equal("Extracted description.", stored.Description);
        Assert.Equal(ItemStatus.Unread, stored.Status);
        Assert.Equal("bookmark", stored.Source);

        // items:index must contain the new id
        var index = await kvStore.GetJsonAsync("items:index", InboxJsonContext.Default.StringArray,
            CancellationToken.None);
        Assert.NotNull(index);
        Assert.Contains(result.Id, index);
    }

    [Fact]
    public async Task PostItem_falls_back_to_url_when_fetch_returns_no_content_type()
    {
        // InMemoryWasiHttpClient returns 200 + empty body with no content-type when no stub matches.
        // The content-type gate must reject this and fall back to URL-as-title.
        var (app, _, _, _) = InboxTestApp.Create();

        var reqBody = InboxTestApp.ToJsonBytes(
            new PostItemRequest { Url = "https://example.com/article" },
            InboxJsonContext.Default.PostItemRequest);
        var response = await InboxTestApp.MutateAsync(app, "POST", "/api/items", reqBody);

        Assert.Equal(200, response.Status);

        var result = InboxTestApp.FromJsonBody(response, InboxJsonContext.Default.PostItemResponse);
        Assert.NotNull(result);
        // No content-type → URL used as title (fail-open)
        Assert.Equal("https://example.com/article", result.Title);
        Assert.Null(result.Description);
    }

    [Fact]
    public async Task PostItem_falls_back_to_url_on_non_2xx_response()
    {
        var (app, _, http, _) = InboxTestApp.Create();

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
        // Non-2xx → URL used as title (fail-open)
        Assert.Equal("https://example.com/article", result.Title);
        Assert.Null(result.Description);
    }

    [Fact]
    public async Task PostItem_appends_to_existing_index()
    {
        var (app, kv, _, _) = InboxTestApp.Create();

        var req1 = InboxTestApp.ToJsonBytes(new PostItemRequest { Url = "https://a.com" }, InboxJsonContext.Default.PostItemRequest);
        var req2 = InboxTestApp.ToJsonBytes(new PostItemRequest { Url = "https://b.com" }, InboxJsonContext.Default.PostItemRequest);

        var r1 = InboxTestApp.FromJsonBody(
            await InboxTestApp.MutateAsync(app, "POST", "/api/items", req1),
            InboxJsonContext.Default.PostItemResponse)!;
        var r2 = InboxTestApp.FromJsonBody(
            await InboxTestApp.MutateAsync(app, "POST", "/api/items", req2),
            InboxJsonContext.Default.PostItemResponse)!;

        IKeyValueStore kvStore2 = kv;
        var index = await kvStore2.GetJsonAsync("items:index", InboxJsonContext.Default.StringArray,
            CancellationToken.None);
        Assert.NotNull(index);
        Assert.Equal(2, index.Length);
        Assert.Equal(r1.Id, index[0]);
        Assert.Equal(r2.Id, index[1]);
    }
}
