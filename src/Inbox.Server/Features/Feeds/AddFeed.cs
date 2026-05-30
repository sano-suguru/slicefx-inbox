using System.ComponentModel.DataAnnotations;
using Inbox.Contracts;
using Inbox.Server.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using SliceFx.Wasi;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Features.Feeds;

[Feature("POST /api/feeds", Summary = "Subscribe to an RSS or Atom feed")]
public static class AddFeed
{
    public record Request([Required, Url] string FeedUrl);

    public record Response(string Id, string FeedUrl, DateTimeOffset AddedAt);

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
        var subscription = new FeedSubscription(id, req.FeedUrl, null, DateTimeOffset.UtcNow);
        await kv.SetJsonAsync($"feed:{id}", subscription, InboxJsonContext.Default.FeedSubscription, ct);

        var index = await kv.GetJsonAsync("feeds:index", InboxJsonContext.Default.StringArray, ct) ?? [];
        await kv.SetJsonAsync("feeds:index", [.. index, id], InboxJsonContext.Default.StringArray, ct);

        return SliceResult.Ok(new Response(id, req.FeedUrl, subscription.AddedAt), InboxJsonContext.Default.AddFeedResponse);
    }
}
