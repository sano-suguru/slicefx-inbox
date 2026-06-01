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

        var result = await RefreshWorkspaceAsync(http, kv, wid, ct);
        return SliceResult<RefreshFeedsResponse>.Ok(result);
    }

    /// <summary>
    /// Refresh feeds for all workspaces listed in <c>workspaces:index</c>.
    /// Called by the cron handler (local Spin) and by <see cref="RefreshAllFeeds"/> (admin HTTP endpoint).
    /// This method is in a compiled-in-all-builds class so tests can call it directly
    /// (FeedRefreshCronHandler is excluded from non-WASI builds).
    /// </summary>
    public static async Task<RefreshFeedsResponse> RefreshAllWorkspacesAsync(
        IWasiHttpClient http, IKeyValueStore kv, CancellationToken ct)
    {
        var widIndex = await kv.GetJsonAsync(WorkspaceKeys.WorkspacesIndex, InboxJsonContext.Default.StringArray, ct) ?? [];

        var totalFeedsChecked = 0;
        var totalItemsAdded = 0;
        var totalSkipped = 0;
        var totalFailed = 0;

        foreach (var wid in widIndex)
        {
            var result = await RefreshWorkspaceAsync(http, kv, wid, ct);
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
    public static async Task<RefreshFeedsResponse> RefreshWorkspaceAsync(
        IWasiHttpClient http, IKeyValueStore kv, string wid, CancellationToken ct)
    {
        var feedIndex = await kv.GetJsonAsync(WorkspaceKeys.FeedsIndex(wid), InboxJsonContext.Default.StringArray, ct) ?? [];

        // Load existing item URLs into a HashSet for O(1) duplicate detection.
        var existingUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var itemIndex = await kv.GetJsonAsync(WorkspaceKeys.ItemsIndex(wid), InboxJsonContext.Default.StringArray, ct) ?? [];
        foreach (var itemId in itemIndex)
        {
            var existingItem = await kv.GetJsonAsync(WorkspaceKeys.Item(wid, itemId), InboxJsonContext.Default.InboxItem, ct);
            if (existingItem is not null) existingUrls.Add(existingItem.Url);
        }

        var feedsChecked = 0;
        var itemsAdded = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var feedId in feedIndex)
        {
            var subscription = await kv.GetJsonAsync(WorkspaceKeys.Feed(wid, feedId), InboxJsonContext.Default.FeedSubscription, ct);
            if (subscription is null) continue;

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

                await kv.SetJsonAsync(WorkspaceKeys.Item(wid, newId), item, InboxJsonContext.Default.InboxItem, ct);

                // Re-read the index to reduce (but not eliminate) lost-update races.
                var latestIndex = await kv.GetJsonAsync(WorkspaceKeys.ItemsIndex(wid), InboxJsonContext.Default.StringArray, ct) ?? [];
                await kv.SetJsonAsync(WorkspaceKeys.ItemsIndex(wid), [.. latestIndex, newId], InboxJsonContext.Default.StringArray, ct);

                existingUrls.Add(entry.Link);
                itemsAdded++;
            }
        }

        return new RefreshFeedsResponse(feedsChecked, itemsAdded, skipped, failed);
    }
}
