using Inbox.Contracts;
using SliceFx.Wasi;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Tests;

public class PostItemTests
{
    [Fact]
    public async Task PostItem_creates_item_and_updates_index()
    {
        var (app, kv, _, _) = InboxTestApp.Create();

        var reqBody = InboxTestApp.ToJsonBytes(
            new PostItemRequest { Url = "https://example.com/article" },
            InboxJsonContext.Default.PostItemRequest);
        var response = await InboxTestApp.MutateAsync(app, "POST", "/api/items", reqBody);

        // Returns 200 (SliceResult.Ok), not 201
        Assert.Equal(200, response.Status);

        var result = InboxTestApp.FromJsonBody(response, InboxJsonContext.Default.PostItemResponse);
        Assert.NotNull(result);
        Assert.NotEmpty(result.Id);
        // OG fetch disabled: URL is used as title
        Assert.Equal("https://example.com/article", result.Url);
        Assert.Equal("https://example.com/article", result.Title);
        Assert.Null(result.Description);

        // item:{id} must exist in KV
        IKeyValueStore kvStore = kv;
        var stored = await kvStore.GetJsonAsync($"item:{result.Id}", InboxJsonContext.Default.InboxItem,
            CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal(result.Id, stored.Id);
        Assert.Equal(ItemStatus.Unread, stored.Status);
        Assert.Equal("bookmark", stored.Source);

        // items:index must contain the new id
        var index = await kvStore.GetJsonAsync("items:index", InboxJsonContext.Default.StringArray,
            CancellationToken.None);
        Assert.NotNull(index);
        Assert.Contains(result.Id, index);
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
