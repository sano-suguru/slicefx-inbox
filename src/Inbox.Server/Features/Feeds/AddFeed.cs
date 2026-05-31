using Inbox.Contracts;
using Inbox.Server.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Features.Feeds;

[Feature("POST /api/feeds", Summary = "Subscribe to an RSS or Atom feed")]
public static class AddFeed
{
    public static async Task<SliceResult<AddFeedResponse>> Handle(
        AddFeedRequest req,
        [FromHeader(Name = "X-Refresh-Token")] string? token,
        ITokenGuard guard,
        IKeyValueStore kv,
        CancellationToken ct)
    {
        if (!await guard.IsAuthorizedAsync(token, ct))
            return SliceResult<AddFeedResponse>.Unauthorized();

        var id = Guid.NewGuid().ToString("N");
        var subscription = new FeedSubscription(id, req.FeedUrl, null, DateTimeOffset.UtcNow);
        await kv.SetJsonAsync($"feed:{id}", subscription, InboxJsonContext.Default.FeedSubscription, ct);

        var index = await kv.GetJsonAsync("feeds:index", InboxJsonContext.Default.StringArray, ct) ?? [];
        await kv.SetJsonAsync("feeds:index", [.. index, id], InboxJsonContext.Default.StringArray, ct);

        return SliceResult<AddFeedResponse>.Ok(new AddFeedResponse(id, req.FeedUrl, subscription.AddedAt));
    }
}
