using Inbox.Contracts;
using Inbox.Server.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using SliceFx.Wasi;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Features.Items;

[Feature("PATCH /api/items/{id}", Summary = "Update status and/or tags on an inbox item")]
public static class UpdateItem
{
    public static async Task<WasiResponse> Handle(
        string id,
        UpdateItemRequest req,
        [FromHeader(Name = "X-Refresh-Token")] string? token,
        ITokenGuard guard,
        IKeyValueStore kv,
        CancellationToken ct)
    {
        if (!await guard.IsAuthorizedAsync(token, ct))
            return SliceResult.Unauthorized();

        var item = await kv.GetJsonAsync($"item:{id}", InboxJsonContext.Default.InboxItem, ct);
        if (item is null) return SliceResult.Problem(404, "Not Found", $"Item '{id}' not found.");

        if (req.Status is not null && req.Status != ItemStatus.Unread
                                   && req.Status != ItemStatus.Read
                                   && req.Status != ItemStatus.Archived)
            return SliceResult.Problem(400, "Bad Request", $"Invalid status '{req.Status}'. Must be one of: unread, read, archived.");

        var updated = item with
        {
            Status = req.Status ?? item.Status,
            Tags = req.Tags ?? item.Tags,
        };
        await kv.SetJsonAsync($"item:{id}", updated, InboxJsonContext.Default.InboxItem, ct);

        return SliceResult.NoContent();
    }
}
