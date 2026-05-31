using Inbox.Contracts;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Features.Items;

[Feature("GET /api/items/{id}", Summary = "Get a single inbox item")]
public static class GetItem
{
    public static async Task<SliceResult<GetItemResponse>> Handle(string id, IKeyValueStore kv, CancellationToken ct)
    {
        var item = await kv.GetJsonAsync($"item:{id}", InboxJsonContext.Default.InboxItem, ct);
        if (item is null) return SliceResult<GetItemResponse>.NotFound($"Item '{id}' not found.");

        return SliceResult<GetItemResponse>.Ok(
            new GetItemResponse(item.Id, item.Url, item.Title, item.Description, item.Status, item.SavedAt, item.Source, item.Tags));
    }
}
