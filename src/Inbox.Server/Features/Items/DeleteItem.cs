using Inbox.Contracts;
using Inbox.Server.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Features.Items;

[Feature("DELETE /api/items/{id}", Summary = "Remove an inbox item")]
public static class DeleteItem
{
    public static async Task<SliceResult> Handle(
        string id,
        [FromHeader(Name = "X-Workspace-Token")] string? token,
        IAuthenticator auth,
        IKeyValueStore kv,
        CancellationToken ct)
    {
        var wid = await auth.AuthenticateAsync(token, ct);
        if (wid is null)
            return SliceResult.Unauthorized();

        if (!await kv.ExistsAsync(WorkspaceKeys.Item(wid, id), ct))
            return SliceResult.NotFound($"Item '{id}' not found.");

        await kv.DeleteAsync(WorkspaceKeys.Item(wid, id), ct);

        var index = await kv.GetJsonAsync(WorkspaceKeys.ItemsIndex(wid), InboxJsonContext.Default.StringArray, ct) ?? [];
        await kv.SetJsonAsync(WorkspaceKeys.ItemsIndex(wid), [.. index.Where(x => x != id)], InboxJsonContext.Default.StringArray, ct);

        return SliceResult.NoContent();
    }
}
