using System.Text;
using Inbox.Contracts;
using Inbox.Server.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using SliceFx.Wasi;
using SliceFx.Wasi.HttpClient;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Features.Feeds;

[Feature("POST /api/feeds/refresh", Summary = "Fetch subscribed feeds for the current workspace and ingest new items")]
public static class RefreshFeeds
{
    /// <summary>
    /// HTTP handler — authenticates via X-Workspace-Token then refreshes the caller's workspace only.
    /// </summary>
    public static async Task<SliceResult<RefreshFeedsResponse>> Handle(
        [FromHeader(Name = "X-Workspace-Token")] string? token,
        IAuthenticator auth,
        IWasiHttpClient http,
        IKeyValueStore kv,
        CancellationToken ct)
    {
        var wid = await auth.AuthenticateAsync(token, ct);
        if (wid is null)
            return SliceResult<RefreshFeedsResponse>.Unauthorized();

        var result = await RefreshWorkspaceAsync(http, kv, wid, feedKeys: null, itemKeys: null, ct);
        return SliceResult<RefreshFeedsResponse>.Ok(result);
    }

    /// <summary>
    /// Refresh feeds for all workspaces.
    /// Called by the cron handler (local Spin) and by <see cref="RefreshAllFeeds"/> (admin HTTP endpoint).
    /// Issues a single <see cref="IKeyValueStore.ListKeysAsync"/> call and partitions the result
    /// in memory — avoids O(W × total-keys) blowup that would result from calling ListKeys once
    /// per workspace.
    /// </summary>
    public static async Task<RefreshFeedsResponse> RefreshAllWorkspacesAsync(
        IWasiHttpClient http, IKeyValueStore kv, CancellationToken ct)
    {
        // Single full-store key scan; workspace IDs are read from workspace:{wid} body keys.
        var partitions = await KvScan.PartitionAsync(kv, ct);
        var wids = await KvScan.ListWorkspaceIdsAsync(kv, ct);

        var totalFeedsChecked = 0;
        var totalItemsAdded = 0;
        var totalSkipped = 0;
        var totalFailed = 0;

        foreach (var wid in wids)
        {
            partitions.TryGetValue(wid, out var partition);
            var result = await RefreshWorkspaceAsync(
                http, kv, wid,
                feedKeys: partition?.FeedKeys,
                itemKeys: partition?.ItemKeys,
                ct);
            totalFeedsChecked += result.FeedsChecked;
            totalItemsAdded += result.ItemsAdded;
            totalSkipped += result.Skipped;
            totalFailed += result.Failed;
        }

        return new RefreshFeedsResponse(totalFeedsChecked, totalItemsAdded, totalSkipped, totalFailed);
    }

    /// <summary>
    /// Core per-workspace refresh logic. Fetches all feeds for <paramref name="wid"/> and ingests new items.
    /// </summary>
    /// <param name="feedKeys">
    /// Pre-partitioned feed keys from <see cref="KvScan.PartitionAsync"/> (cron batch path).
    /// When <c>null</c>, feed subscriptions are derived by a fresh <see cref="KvScan.ListFeedsAsync"/> call
    /// (single-workspace HTTP path).
    /// </param>
    /// <param name="itemKeys">
    /// Pre-partitioned item keys from <see cref="KvScan.PartitionAsync"/> (cron batch path).
    /// When <c>null</c>, items are derived by a fresh <see cref="KvScan.ListItemsAsync"/> call.
    /// </param>
    public static async Task<RefreshFeedsResponse> RefreshWorkspaceAsync(
        IWasiHttpClient http, IKeyValueStore kv, string wid,
        IReadOnlyList<string>? feedKeys, IReadOnlyList<string>? itemKeys,
        CancellationToken ct)
    {
        // Load feed subscriptions — from pre-partitioned keys (cron) or a fresh scan (HTTP).
        var feeds = new List<FeedSubscription>();
        if (feedKeys is not null)
        {
            foreach (var key in feedKeys)
            {
                var feed = await kv.GetJsonAsync(key, InboxJsonContext.Default.FeedSubscription, ct);
                if (feed is not null) feeds.Add(feed);
            }
        }
        else
        {
            feeds.AddRange(await KvScan.ListFeedsAsync(kv, wid, ct));
        }

        // Load existing item URLs into a HashSet for O(1) duplicate detection.
        var existingUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (itemKeys is not null)
        {
            foreach (var key in itemKeys)
            {
                var existingItem = await kv.GetJsonAsync(key, InboxJsonContext.Default.InboxItem, ct);
                if (existingItem is not null) existingUrls.Add(existingItem.Url);
            }
        }
        else
        {
            foreach (var existingItem in await KvScan.ListItemsAsync(kv, wid, ct))
                existingUrls.Add(existingItem.Url);
        }

        var feedsChecked = 0;
        var itemsAdded = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var subscription in feeds)
        {
            feedsChecked++;

            WasiResponse fetchResult;
            try
            {
                fetchResult = await http.SendAsync(
                    new WasiHttpRequest("GET", subscription.FeedUrl, null, null), ct);
            }
            catch (WasiHttpException ex)
            {
                Console.Error.WriteLine($"[RefreshFeeds/{wid}] transport error for {subscription.FeedUrl}: {ex.Message}");
                failed++;
                continue;
            }

            if (fetchResult.Status is < 200 or >= 300)
            {
                Console.Error.WriteLine($"[RefreshFeeds/{wid}] HTTP {fetchResult.Status} for {subscription.FeedUrl}");
                failed++;
                continue;
            }

            string xml;
            try
            {
                xml = Encoding.UTF8.GetString(fetchResult.Body);
            }
            catch (Exception)
            {
                failed++;
                continue;
            }

            IReadOnlyList<ParsedEntry> entries;
            try
            {
                entries = FeedParser.Parse(xml);
            }
            catch (Exception)
            {
                failed++;
                continue;
            }

            var now = DateTimeOffset.UtcNow;
            foreach (var entry in entries)
            {
                if (existingUrls.Contains(entry.Link))
                {
                    skipped++;
                    continue;
                }

                var newId = Guid.NewGuid().ToString("N");
                var item = new InboxItem(
                    newId,
                    entry.Link,
                    string.IsNullOrWhiteSpace(entry.Title) ? entry.Link : entry.Title,
                    null,
                    ItemStatus.Unread,
                    entry.Published ?? now,
                    "rss");

                // Single-key write — no index update needed. KvScan.ListItemsAsync derives the
                // listing by prefix-scanning w:{wid}:item:* keys, eliminating the former
                // read-modify-write race on the items:index key.
                await kv.SetJsonAsync(WorkspaceKeys.Item(wid, newId), item, InboxJsonContext.Default.InboxItem, ct);

                existingUrls.Add(entry.Link);
                itemsAdded++;
            }
        }

        return new RefreshFeedsResponse(feedsChecked, itemsAdded, skipped, failed);
    }
}
