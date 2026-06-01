using Inbox.Contracts;
using Inbox.Server.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Features.Feeds;

[Feature("GET /api/feeds", Summary = "List feed subscriptions for the current workspace")]
public static class GetFeeds
{
    public static async Task<SliceResult<GetFeedsResponse>> Handle(
        [FromHeader(Name = "X-Workspace-Token")] string? token,
        IAuthenticator auth,
        IKeyValueStore kv,
        CancellationToken ct)
    {
        var wid = await auth.AuthenticateAsync(token, ct);
        if (wid is null)
            return SliceResult<GetFeedsResponse>.Unauthorized();

        var index = await kv.GetJsonAsync(WorkspaceKeys.FeedsIndex(wid), InboxJsonContext.Default.StringArray, ct) ?? [];

        var feeds = new List<FeedSubscription>(index.Length);
        foreach (var id in index)
        {
            var feed = await kv.GetJsonAsync(WorkspaceKeys.Feed(wid, id), InboxJsonContext.Default.FeedSubscription, ct);
            if (feed is not null) feeds.Add(feed);
        }

        var result = feeds.ToArray();
        return SliceResult<GetFeedsResponse>.Ok(new GetFeedsResponse(result, result.Length));
    }
}
