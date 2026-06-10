using System.Text;
using Inbox.Contracts;
using Inbox.Server.Filters;
using Inbox.Server.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using SliceFx.Wasi;
using SliceFx.Wasi.HttpClient;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Features.Feeds;

[Feature("POST /api/feeds/refresh", Summary = "Fetch subscribed feeds for the current workspace and ingest new items")]
[SliceFilter<WorkspaceAuthFilter>]
public static class RefreshFeeds
{
    /// <summary>Maximum number of new entries ingested per feed per refresh sweep.</summary>
    internal const int MaxEntriesPerRefresh = 100;

    /// <summary>Maximum total items a workspace may accumulate. Refresh is skipped when reached.</summary>
    internal const int MaxItemsPerWorkspace = 2000;

    /// <summary>
    /// HTTP handler — authenticates via X-Workspace-Token then refreshes the caller's workspace only.
    /// </summary>
    public static async Task<SliceResult<RefreshFeedsResponse>> Handle(
        [FromServices] CurrentWorkspace ws,
        IWasiHttpClient http,
        IKeyValueStore kv,
        CancellationToken ct)
    {
        var wid = ws.WorkspaceId;

        var result = await RefreshWorkspaceAsync(http, kv, wid, feedKeys: null, itemKeys: null, ct);
        return SliceResult<RefreshFeedsResponse>.Ok(result);
    }

    /// <summary>
    /// Refresh feeds for all workspaces.
    /// Called by the cron handler (local Spin) and by <see cref="RefreshAllFeeds"/> (admin HTTP endpoint).
    /// Issues a single <see cref="IKeyValueStore.ListKeysAsync"/> call via
    /// <see cref="KvScan.PartitionAsync"/> and iterates over the result in memory —
    /// avoids O(W × total-keys) blowup and the redundant second full scan that
    /// <see cref="KvScan.ListWorkspaceIdsAsync"/> would otherwise require.
    /// Workspaces with no feed subscriptions are skipped (nothing to refresh).
    /// Per-workspace exceptions are caught and logged so one poisoned workspace cannot abort the sweep.
    /// </summary>
    internal static async Task<RefreshFeedsResponse> RefreshAllWorkspacesAsync(
        IWasiHttpClient http, IKeyValueStore kv, CancellationToken ct)
    {
        // Single full-store key scan. PartitionAsync returns only workspaces that
        // have at least one w:{wid}:item:* or w:{wid}:feed:* key; empty workspaces
        // (just created, no data yet) produce no entry and are correctly skipped.
        var partitions = await KvScan.PartitionAsync(kv, ct);

        var totalFeedsChecked = 0;
        var totalItemsAdded = 0;
        var totalSkipped = 0;
        var totalFailed = 0;

        foreach (var partition in partitions.Values)
        {
            if (partition.FeedKeys.Count == 0) continue; // no subscriptions — nothing to refresh

            try
            {
                var result = await RefreshWorkspaceAsync(
                    http, kv, partition.Wid,
                    feedKeys: partition.FeedKeys,
                    itemKeys: partition.ItemKeys,
                    ct);
                totalFeedsChecked += result.FeedsChecked;
                totalItemsAdded += result.ItemsAdded;
                totalSkipped += result.Skipped;
                totalFailed += result.Failed;
            }
            catch (Exception ex)
            {
                // Workspace-level failure (e.g. corrupt KV blob outside the per-feed guard).
                // Log and continue — one poisoned workspace must not abort subsequent workspaces.
                Console.Error.WriteLine(
                    $"[RefreshFeeds] workspace '{partition.Wid}' failed during sweep; skipping. {ex.Message}");
            }
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
    private static async Task<RefreshFeedsResponse> RefreshWorkspaceAsync(
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
                FeedSubscription? feed;
                try
                {
                    feed = await kv.GetJsonAsync(key, InboxJsonContext.Default.FeedSubscription, ct);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[RefreshFeeds/{wid}] corrupt feed blob at '{key}'; skipping. {ex.Message}");
                    continue;
                }
                if (feed is not null) feeds.Add(feed);
            }
        }
        else
        {
            feeds.AddRange(await KvScan.ListFeedsAsync(kv, wid, ct));
        }

        // Load existing item URLs into a HashSet for O(1) duplicate detection.
        // Normalized form (lowercase scheme+host, trailing slash stripped) to avoid
        // re-ingesting the same article when feeds change trailing slashes / casing.
        // The saved URL in InboxItem is always the original; normalization is comparison-only.
        var existingUrls = new HashSet<string>(StringComparer.Ordinal);
        if (itemKeys is not null)
        {
            foreach (var key in itemKeys)
            {
                InboxItem? existingItem;
                try
                {
                    existingItem = await kv.GetJsonAsync(key, InboxJsonContext.Default.InboxItem, ct);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[RefreshFeeds/{wid}] corrupt item blob at '{key}'; skipping. {ex.Message}");
                    continue;
                }
                if (existingItem is not null) existingUrls.Add(NormalizeUrlForDedup(existingItem.Url));
            }
        }
        else
        {
            foreach (var existingItem in await KvScan.ListItemsAsync(kv, wid, ct))
                existingUrls.Add(NormalizeUrlForDedup(existingItem.Url));
        }

        // Early exit when the workspace has hit its item cap.
        if (existingUrls.Count >= MaxItemsPerWorkspace)
        {
            Console.Error.WriteLine(
                $"[RefreshFeeds/{wid}] item cap ({MaxItemsPerWorkspace}) reached; skipping refresh.");
            return new RefreshFeedsResponse(0, 0, 0, 0);
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
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[RefreshFeeds/{wid}] UTF-8 decode error for {subscription.FeedUrl}: {ex.Message}");
                failed++;
                continue;
            }

            ParsedFeed parsed;
            try
            {
                parsed = FeedParser.Parse(xml);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[RefreshFeeds/{wid}] parse error for {subscription.FeedUrl}: {ex.Message}");
                failed++;
                continue;
            }

            // Back-fill the subscription title from the feed's channel/feed-level <title> when
            // the stored title is null (e.g. feeds added via POST /api/feeds before this feature).
            // Existing titles are never overwritten.
            if (subscription.Title is null && parsed.FeedTitle is not null)
            {
                var updatedSubscription = subscription with { Title = parsed.FeedTitle };
                await kv.SetJsonAsync(
                    WorkspaceKeys.Feed(wid, subscription.Id),
                    updatedSubscription,
                    InboxJsonContext.Default.FeedSubscription,
                    ct);
            }

            var now = DateTimeOffset.UtcNow;
            var entriesThisFeed = 0;
            foreach (var entry in parsed.Entries)
            {
                // Per-feed entry cap to guard against unexpectedly large feeds.
                if (entriesThisFeed >= MaxEntriesPerRefresh) break;
                // Workspace item cap: check during sweep so growth across feeds is tracked.
                if (existingUrls.Count >= MaxItemsPerWorkspace) break;

                if (existingUrls.Contains(NormalizeUrlForDedup(entry.Link)))
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

                existingUrls.Add(NormalizeUrlForDedup(entry.Link));
                itemsAdded++;
                entriesThisFeed++;
            }
        }

        return new RefreshFeedsResponse(feedsChecked, itemsAdded, skipped, failed);
    }

    /// <summary>
    /// Normalizes a URL for deduplication comparison only. The stored URL in <see cref="InboxItem"/>
    /// is always the original value; this is used solely as the HashSet key.
    /// Normalizations applied: lowercase scheme and host, trailing slash stripped from path.
    /// Falls back to the raw URL if parsing fails.
    /// </summary>
    private static string NormalizeUrlForDedup(string url)
    {
        if (string.IsNullOrEmpty(url)) return url;
        try
        {
            var uri = new Uri(url);
            // Lowercase scheme + host; preserve path casing; strip trailing slash (except bare root).
            var path = uri.AbsolutePath.Length > 1
                ? uri.AbsolutePath.TrimEnd('/')
                : uri.AbsolutePath;
            return $"{uri.Scheme.ToLowerInvariant()}://{uri.Authority.ToLowerInvariant()}{path}{uri.Query}";
        }
        catch (UriFormatException)
        {
            return url;
        }
    }
}
