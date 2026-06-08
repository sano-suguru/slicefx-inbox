using Inbox.Contracts;
using Inbox.Server.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Features.Share;

/// <summary>
/// Creates a public share link for an item owned by the authenticated workspace.
/// Idempotent: calling twice for the same item returns the same token.
/// </summary>
/// <remarks>
/// <para>
/// Write order: forward key <c>w:{wid}:item:{id}:share</c> is written first,
/// then reverse key <c>share:{token}</c>. The reverse key being present is the
/// condition that makes the item publicly readable — a crash between the two writes
/// leaves only the forward key (idempotency marker), which is harmless.
/// </para>
/// <para>
/// The server returns only the token; the caller composes the full URL
/// (<c>{baseUrl}/s/{token}</c>) to avoid hard-coding the deployment host.
/// </para>
/// <para>
/// Token entropy: <c>Guid.NewGuid().ToString("N")</c> = 32 hex chars, ~122 random bits.
/// Fermyon KV has no TTL — the token remains valid until explicitly revoked via
/// <c>DELETE /api/items/{id}/share</c>.
/// </para>
/// </remarks>
[Feature("POST /api/items/{id}/share", Summary = "Create a public share link for an item")]
public static class CreateShare
{
    public static async Task<SliceResult<ShareResponse>> Handle(
        string id,
        [FromHeader(Name = "X-Workspace-Token")] string? token,
        IAuthenticator auth,
        IKeyValueStore kv,
        CancellationToken ct)
    {
        var wid = await auth.AuthenticateAsync(token, ct);
        if (wid is null)
            return SliceResult<ShareResponse>.Unauthorized();

        // Verify the item exists before creating a share.
        if (!await kv.ExistsAsync(WorkspaceKeys.Item(wid, id), ct))
            return SliceResult<ShareResponse>.NotFound($"Item '{id}' not found.");

        // Idempotent: if a share already exists for this item, return the same token.
        var existing = await kv.GetStringAsync(WorkspaceKeys.ItemShare(wid, id), ct);
        if (existing is not null)
            return SliceResult<ShareResponse>.Ok(new ShareResponse(existing));

        // Generate a new share token and persist both KV keys.
        var shareToken = Guid.NewGuid().ToString("N");

        // Write forward key first (idempotency marker). The reverse key being absent
        // means the item is still private even if the forward key is present.
        await kv.SetStringAsync(WorkspaceKeys.ItemShare(wid, id), shareToken, ct);

        // Write reverse key last — this is the "publish" moment.
        await kv.SetStringAsync(WorkspaceKeys.Share(shareToken), $"{wid}:{id}", ct);

        return SliceResult<ShareResponse>.Ok(new ShareResponse(shareToken));
    }
}
