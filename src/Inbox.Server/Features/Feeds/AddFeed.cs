using Inbox.Contracts;
using Inbox.Server.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Features.Feeds;

[Feature("POST /api/feeds", Summary = "Subscribe to an RSS or Atom feed")]
public static class AddFeed
{
    /// <summary>Maximum RSS/Atom feed subscriptions per workspace.</summary>
    internal const int MaxFeedsPerWorkspace = 50;

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

        // Reject non-https feed URLs. [Url] DataAnnotation accepts http/ftp; we tighten here
        // to prevent zombie subscriptions that always fail refresh (allowed_outbound_hosts is https-only).
        if (!req.FeedUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return SliceResult<AddFeedResponse>.BadRequest("Feed URL must use the https:// scheme.");

        // Demo workspace: feed management is restricted to prevent anonymous server-side fetch
        // amplification (arbitrary https URLs via the shared public token).
        if (wid == DemoWorkspace.Wid)
            return SliceResult<AddFeedResponse>.Problem(
                403, "Demo restriction", "Feeds cannot be added to the demo workspace.");

        // Per-workspace feed cap — key-count only (no body fetch) to mirror CountWorkspacesAsync cost.
        var feedCount = await KvScan.CountFeedKeysAsync(kv, wid, ct);
        if (feedCount >= MaxFeedsPerWorkspace)
            return SliceResult<AddFeedResponse>.Problem(
                429, "Feed limit reached",
                $"Workspace may not exceed {MaxFeedsPerWorkspace} feed subscriptions.");

        var id = Guid.NewGuid().ToString("N");
        var subscription = new FeedSubscription(id, req.FeedUrl, null, DateTimeOffset.UtcNow);
        // Single-key write — no index update needed. KvScan.ListFeedsAsync derives the listing
        // by prefix-scanning w:{wid}:feed:* keys, eliminating the former read-modify-write race.
        await kv.SetJsonAsync(WorkspaceKeys.Feed(wid, id), subscription, InboxJsonContext.Default.FeedSubscription, ct);

        return SliceResult<AddFeedResponse>.Ok(new AddFeedResponse(id, req.FeedUrl, subscription.AddedAt));
    }
}
