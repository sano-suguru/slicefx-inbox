using System.ComponentModel.DataAnnotations;
using Inbox.Contracts;
using Inbox.Server.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using SliceFx.Wasi;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Features.Items;

// SPIKE_1 RESULT (confirmed 2026-05-29):
// HttpClient.GetStringAsync is genuinely async; the WASI single-thread model cannot block on a pending
// Task via GetAwaiter().GetResult(). Outbound HTTP fetch is disabled for Increment A.
// Next step: create SliceFx.Wasi.HttpClient satellite that calls wasi:http/outgoing-handler
// via synchronous WIT bindings instead of going through the async HttpClient stack.

[Feature("POST /api/items", Summary = "Save a URL for later reading")]
public static class PostItem
{
    public record Request([Required, Url] string Url);

    public record Response(string Id, string Url, string Title, string? Description, DateTimeOffset SavedAt);

    public static async Task<WasiResponse> Handle(
        Request req,
        [FromHeader(Name = "X-Refresh-Token")] string? token,
        ITokenGuard guard,
        IKeyValueStore kv,
        CancellationToken ct)
    {
        if (!await guard.IsAuthorizedAsync(token, ct))
            return SliceResult.Unauthorized();

        var id = Guid.NewGuid().ToString("N");
        // OG title fetch disabled (Spike 1 result: HttpClient is incompatible with WASI dispatch).
        // URL is used as the title until SliceFx.Wasi.HttpClient satellite is available.
        var item = new InboxItem(id, req.Url, req.Url, null, "unread", DateTimeOffset.UtcNow, "bookmark");
        await kv.SetJsonAsync($"item:{id}", item, InboxJsonContext.Default.InboxItem, ct);

        var index = await kv.GetJsonAsync("items:index", InboxJsonContext.Default.StringArray, ct) ?? [];
        await kv.SetJsonAsync("items:index", [.. index, id], InboxJsonContext.Default.StringArray, ct);

        return SliceResult.Ok(new Response(id, req.Url, req.Url, null, item.SavedAt), InboxJsonContext.Default.PostItemResponse);
    }
}
