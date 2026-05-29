using Inbox.Server.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using SliceFx.Wasi;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Features.Items;

[Feature("DELETE /api/items/{id}", Summary = "Remove an inbox item")]
public static class DeleteItem
{
    public static async Task<WasiResponse> Handle(
        string id,
        [FromHeader(Name = "X-Refresh-Token")] string? token,
        ISecrets secrets,
        IKeyValueStore kv,
        CancellationToken ct)
    {
        if (!TokenAuth.SafeEquals(token, secrets.RefreshToken))
            return SliceResult.Unauthorized();

        if (!await kv.ExistsAsync($"item:{id}", ct))
            return SliceResult.Problem(404, "Not Found", $"Item '{id}' not found.");

        await kv.DeleteAsync($"item:{id}", ct);

        var index = await kv.GetJsonAsync("items:index", InboxJsonContext.Default.StringArray, ct) ?? [];
        await kv.SetJsonAsync("items:index", [.. index.Where(x => x != id)], InboxJsonContext.Default.StringArray, ct);

        return SliceResult.NoContent();
    }
}
