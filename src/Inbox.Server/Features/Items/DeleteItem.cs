using Inbox.Contracts;
using Inbox.Server.Filters;
using Inbox.Server.Infrastructure;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Features.Items;

[Feature("DELETE /api/items/{id}", Summary = "Remove an inbox item")]
[SliceFilter<WorkspaceAuthFilter>]
public static class DeleteItem
{
    public static async Task<SliceResult> Handle(
        string id,
        CurrentWorkspace ws,
        IKeyValueStore kv,
        CancellationToken ct)
    {
        var wid = ws.WorkspaceId;

        if (!await kv.ExistsAsync(WorkspaceKeys.Item(wid, id), ct))
            return SliceResult.NotFound($"Item '{id}' not found.");

        // Clean up any public share before deleting the item, so that the share page
        // immediately returns 404 rather than serving a ghost entry.
        var shareToken = await kv.GetStringAsync(WorkspaceKeys.ItemShare(wid, id), ct);
        if (shareToken is not null)
        {
            await kv.DeleteAsync(WorkspaceKeys.Share(shareToken), ct);
            await kv.DeleteAsync(WorkspaceKeys.ItemShare(wid, id), ct);
        }

        // Single-key delete — no index update needed. Listing is derived by prefix scan.
        await kv.DeleteAsync(WorkspaceKeys.Item(wid, id), ct);

        return SliceResult.NoContent();
    }
}
