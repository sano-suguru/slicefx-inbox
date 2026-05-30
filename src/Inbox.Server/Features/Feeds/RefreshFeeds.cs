using System.Text;
using Inbox.Contracts;
using Inbox.Server.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using SliceFx.Wasi;
using SliceFx.Wasi.HttpClient;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Features.Feeds;

[Feature("POST /api/feeds/refresh", Summary = "Fetch all subscribed feeds and ingest new items")]
public static class RefreshFeeds
{
    public record Response(int FeedsChecked, int ItemsAdded, int Skipped, int Failed);

    /// <summary>
    /// HTTP handler — authenticates via X-Refresh-Token header then delegates to <see cref="RefreshAllAsync"/>.
    /// </summary>
    public static async Task<WasiResponse> Handle(
        [FromHeader(Name = "X-Refresh-Token")] string? token,
        ITokenGuard guard,
        IWasiHttpClient http,
        IKeyValueStore kv,
        CancellationToken ct)
    {
        if (!await guard.IsAuthorizedAsync(token, ct))
            return SliceResult.Unauthorized();

        var result = await RefreshAllAsync(http, kv, ct);
        return SliceResult.Ok(result, InboxJsonContext.Default.RefreshFeedsResponse);
    }

    /// <summary>
    /// Core refresh logic — invokable from both the HTTP handler and the cron path.
    /// The cron path is server-side trusted and skips auth entirely.
    /// </summary>
    public static async Task<Response> RefreshAllAsync(IWasiHttpClient http, IKeyValueStore kv, CancellationToken ct)
    {
        // Load the list of subscribed feeds.
        var feedIndex = await kv.GetJsonAsync("feeds:index", InboxJsonContext.Default.StringArray, ct) ?? [];

        // Load the existing item URLs into a HashSet for O(1) duplicate detection.
        var existingUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var itemIndex = await kv.GetJsonAsync("items:index", InboxJsonContext.Default.StringArray, ct) ?? [];
        foreach (var itemId in itemIndex)
        {
            var existingItem = await kv.GetJsonAsync($"item:{itemId}", InboxJsonContext.Default.InboxItem, ct);
            if (existingItem is not null) existingUrls.Add(existingItem.Url);
        }

        var feedsChecked = 0;
        var itemsAdded = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var feedId in feedIndex)
        {
            var subscription = await kv.GetJsonAsync($"feed:{feedId}", InboxJsonContext.Default.FeedSubscription, ct);
            if (subscription is null) continue;

            feedsChecked++;

            // Fetch the feed. One failure must not abort the whole batch.
            WasiResponse fetchResult;
            try
            {
                fetchResult = await http.SendAsync(
                    new WasiHttpRequest(
                        "GET",
                        subscription.FeedUrl,
                        // Pass headers via WasiHttpRequest; SpinWasiHttpClient adds User-Agent + Accept.
                        // null means "no extra headers beyond defaults".
                        null,
                        null),
                    ct);
            }
            catch (WasiHttpException ex)
            {
                Console.Error.WriteLine($"[RefreshFeeds] fetch transport error for {subscription.FeedUrl}: {ex.Message}");
                failed++;
                continue;
            }

            if (fetchResult.Status is < 200 or >= 300)
            {
                Console.Error.WriteLine($"[RefreshFeeds] HTTP {fetchResult.Status} for {subscription.FeedUrl}");
                failed++;
                continue;
            }

            // Parse the feed XML.
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

            // Ingest new entries; skip duplicates by URL.
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
                    "unread",
                    entry.Published ?? now,
                    "rss");

                await kv.SetJsonAsync($"item:{newId}", item, InboxJsonContext.Default.InboxItem, ct);

                // Re-read the index to avoid lost-update races (Spin KV is single-threaded per request,
                // but index may grow within this loop).
                var latestIndex = await kv.GetJsonAsync("items:index", InboxJsonContext.Default.StringArray, ct) ?? [];
                await kv.SetJsonAsync("items:index", [.. latestIndex, newId], InboxJsonContext.Default.StringArray, ct);

                existingUrls.Add(entry.Link);
                itemsAdded++;
            }
        }

        return new Response(feedsChecked, itemsAdded, skipped, failed);
    }
}
