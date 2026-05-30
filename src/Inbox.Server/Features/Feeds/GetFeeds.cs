using Inbox.Contracts;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Features.Feeds;

[Feature("GET /api/feeds", Summary = "List feed subscriptions")]
public static class GetFeeds
{
    public static async Task<GetFeedsResponse> Handle(IKeyValueStore kv, CancellationToken ct)
    {
        var index = await kv.GetJsonAsync("feeds:index", InboxJsonContext.Default.StringArray, ct) ?? [];

        var feeds = new List<FeedSubscription>(index.Length);
        foreach (var id in index)
        {
            var feed = await kv.GetJsonAsync($"feed:{id}", InboxJsonContext.Default.FeedSubscription, ct);
            if (feed is not null) feeds.Add(feed);
        }

        var result = feeds.ToArray();
        return new GetFeedsResponse(result, result.Length);
    }
}
