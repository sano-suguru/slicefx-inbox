using Inbox.Contracts;
using Inbox.Server.Infrastructure;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Tests;

public class UpdateDeleteItemTests
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
    public async Task UpdateItem_returns_404_for_missing_id()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();
        var body = InboxTestApp.ToJsonBytes(
            new UpdateItemRequest { Status = ItemStatus.Read }, InboxJsonContext.Default.UpdateItemRequest);
        var response = await InboxTestApp.MutateAsync(app, "PATCH", "/api/items/nonexistent", body);
        Assert.Equal(404, response.Status);
    }

    [Fact]
    public async Task UpdateItem_returns_400_for_invalid_status()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();
        var id = await CreateItemAsync(app);
        var body = InboxTestApp.ToJsonBytes(
            new UpdateItemRequest { Status = "invalid-status" }, InboxJsonContext.Default.UpdateItemRequest);
        var response = await InboxTestApp.MutateAsync(app, "PATCH", $"/api/items/{id}", body);
        Assert.Equal(400, response.Status);
    }

    [Fact]
    public async Task UpdateItem_updates_status_preserving_unspecified_fields()
    {
        var (app, kv, _, _, _) = InboxTestApp.Create();

        var id = await CreateItemAsync(app);
        var tagBody = InboxTestApp.ToJsonBytes(
            new UpdateItemRequest { Tags = ["tech"] }, InboxJsonContext.Default.UpdateItemRequest);
        await InboxTestApp.MutateAsync(app, "PATCH", $"/api/items/{id}", tagBody);

        var statusBody = InboxTestApp.ToJsonBytes(
            new UpdateItemRequest { Status = ItemStatus.Read }, InboxJsonContext.Default.UpdateItemRequest);
        var updateResp = await InboxTestApp.MutateAsync(app, "PATCH", $"/api/items/{id}", statusBody);
        Assert.Equal(204, updateResp.Status);

        IKeyValueStore kvStore = kv;
        var stored = await kvStore.GetJsonAsync(
            WorkspaceKeys.Item(InboxTestApp.DefaultWid, id), InboxJsonContext.Default.InboxItem,
            CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal(ItemStatus.Read, stored.Status);
        Assert.NotNull(stored.Tags);
        Assert.Contains("tech", stored.Tags);
    }

    [Fact]
    public async Task DeleteItem_returns_404_for_missing_id()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();
        var response = await InboxTestApp.MutateAsync(app, "DELETE", "/api/items/nonexistent");
        Assert.Equal(404, response.Status);
    }

    [Fact]
    public async Task DeleteItem_removes_item_and_updates_index()
    {
        var (app, kv, _, _, _) = InboxTestApp.Create();

        var id1 = await CreateItemAsync(app, "https://a.com");
        var id2 = await CreateItemAsync(app, "https://b.com");

        var response = await InboxTestApp.MutateAsync(app, "DELETE", $"/api/items/{id1}");
        Assert.Equal(204, response.Status);

        IKeyValueStore kvStore = kv;
        var item = await kvStore.GetJsonAsync(
            WorkspaceKeys.Item(InboxTestApp.DefaultWid, id1), InboxJsonContext.Default.InboxItem,
            CancellationToken.None);
        Assert.Null(item);

        // Verify via prefix scan that id1 is gone and id2 remains.
        var items = await KvScan.ListItemsAsync(kvStore, InboxTestApp.DefaultWid, CancellationToken.None);
        Assert.DoesNotContain(items, i => i.Id == id1);
        Assert.Contains(items, i => i.Id == id2);
    }
}
