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
        [FromHeader(Name = "X-Workspace-Token")] string? token,
        IAuthenticator auth,
        IKeyValueStore kv,
        CancellationToken ct)
    {
        var wid = await auth.AuthenticateAsync(token, ct);
        if (wid is null)
            return SliceResult<AddFeedResponse>.Unauthorized();

        var id = Guid.NewGuid().ToString("N");
        var subscription = new FeedSubscription(id, req.FeedUrl, null, DateTimeOffset.UtcNow);
        // Single-key write — no index update needed. KvScan.ListFeedsAsync derives the listing
        // by prefix-scanning w:{wid}:feed:* keys, eliminating the former read-modify-write race.
        await kv.SetJsonAsync(WorkspaceKeys.Feed(wid, id), subscription, InboxJsonContext.Default.FeedSubscription, ct);

        return SliceResult<AddFeedResponse>.Ok(new AddFeedResponse(id, req.FeedUrl, subscription.AddedAt));
    }
}
