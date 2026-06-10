using Inbox.Contracts;
using Inbox.Server.Filters;
using Inbox.Server.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Features.Share;

/// <summary>
/// Revokes the public share link for an item owned by the authenticated workspace.
/// Idempotent — returns 204 even if no share exists.
/// </summary>
/// <remarks>
/// Delete order: reverse key <c>share:{token}</c> is deleted first to immediately
/// make the page return 404, then the forward key is removed.
/// </remarks>
[Feature("DELETE /api/items/{id}/share", Summary = "Revoke a public share link")]
[SliceFilter<WorkspaceAuthFilter>]
public static class RevokeShare
{
    public static async Task<SliceResult> Handle(
        string id,
        [FromServices] CurrentWorkspace ws,
        IKeyValueStore kv,
        CancellationToken ct)
    {
        var wid = ws.WorkspaceId;

        // Look up the share token via the forward key.
        var shareToken = await kv.GetStringAsync(WorkspaceKeys.ItemShare(wid, id), ct);
        if (shareToken is null)
            return SliceResult.NoContent(); // Already revoked — idempotent.

        // Delete reverse key first — immediately stops public access.
        await kv.DeleteAsync(WorkspaceKeys.Share(shareToken), ct);

        // Then remove the forward key (idempotency marker).
        await kv.DeleteAsync(WorkspaceKeys.ItemShare(wid, id), ct);

        return SliceResult.NoContent();
    }
}
