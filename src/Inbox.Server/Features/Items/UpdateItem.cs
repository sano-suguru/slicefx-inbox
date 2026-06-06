using Inbox.Contracts;
using Inbox.Server.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Features.Items;

[Feature("PATCH /api/items/{id}", Summary = "Update status and/or tags on an inbox item")]
public static class UpdateItem
{
    public static async Task<SliceResult> Handle(
        string id,
        UpdateItemRequest req,
        [FromHeader(Name = "X-Workspace-Token")] string? token,
        IAuthenticator auth,
        IKeyValueStore kv,
        CancellationToken ct)
    {
        var wid = await auth.AuthenticateAsync(token, ct);
        if (wid is null)
            return SliceResult.Unauthorized();

        var item = await kv.GetJsonAsync(WorkspaceKeys.Item(wid, id), InboxJsonContext.Default.InboxItem, ct);
        if (item is null) return SliceResult.NotFound($"Item '{id}' not found.");

        if (req.Status is not null && req.Status != ItemStatus.Unread
                                   && req.Status != ItemStatus.Read
                                   && req.Status != ItemStatus.Archived)
            return SliceResult.BadRequest($"Invalid status '{req.Status}'. Must be one of: unread, read, archived.");

        if (req.Tags is not null)
        {
            if (req.Tags.Length > 20)
                return SliceResult.BadRequest("Too many tags. Maximum is 20 per item.");
            foreach (var tag in req.Tags)
            {
                // string.IsNullOrWhiteSpace(null) returns true, so this also guards null elements
                // that System.Text.Json can produce from JSON [null] (STJ does not enforce element
                // non-nullability on string[]). Without this check, tag.Length would throw NRE → 500.
                if (string.IsNullOrWhiteSpace(tag))
                    return SliceResult.BadRequest("Tags must not be null or whitespace.");
                if (tag.Length > 100)
                    return SliceResult.BadRequest("Tag too long. Maximum tag length is 100 characters.");
            }
        }

        var updated = item with
        {
            Status = req.Status ?? item.Status,
            Tags = req.Tags ?? item.Tags,
        };
        await kv.SetJsonAsync(WorkspaceKeys.Item(wid, id), updated, InboxJsonContext.Default.InboxItem, ct);

        return SliceResult.NoContent();
    }
}
