using Inbox.Contracts;
using Inbox.Server.Filters;
using Inbox.Server.Infrastructure;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Features.Items;

[Feature("GET /api/items/{id}", Summary = "Get a single inbox item")]
[SliceFilter<WorkspaceAuthFilter>]
public static class GetItem
{
    public static async Task<SliceResult<GetItemResponse>> Handle(
        string id,
        CurrentWorkspace ws,
        IKeyValueStore kv,
        CancellationToken ct)
    {
        var wid = ws.WorkspaceId;

        var item = await kv.GetJsonAsync(WorkspaceKeys.Item(wid, id), InboxJsonContext.Default.InboxItem, ct);
        if (item is null) return SliceResult<GetItemResponse>.NotFound($"Item '{id}' not found.");

        return SliceResult<GetItemResponse>.Ok(
            new GetItemResponse(item.Id, item.Url, item.Title, item.Description, item.Status, item.SavedAt, item.Source, item.Tags));
    }
}
