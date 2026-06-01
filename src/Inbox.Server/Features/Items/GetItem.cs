using Inbox.Contracts;
using Inbox.Server.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Features.Items;

[Feature("GET /api/items/{id}", Summary = "Get a single inbox item")]
public static class GetItem
{
    public static async Task<SliceResult<GetItemResponse>> Handle(
        string id,
        [FromHeader(Name = "X-Workspace-Token")] string? token,
        IAuthenticator auth,
        IKeyValueStore kv,
        CancellationToken ct)
    {
        var wid = await auth.AuthenticateAsync(token, ct);
        if (wid is null)
            return SliceResult<GetItemResponse>.Unauthorized();

        var item = await kv.GetJsonAsync(WorkspaceKeys.Item(wid, id), InboxJsonContext.Default.InboxItem, ct);
        if (item is null) return SliceResult<GetItemResponse>.NotFound($"Item '{id}' not found.");

        return SliceResult<GetItemResponse>.Ok(
            new GetItemResponse(item.Id, item.Url, item.Title, item.Description, item.Status, item.SavedAt, item.Source, item.Tags));
    }
}
