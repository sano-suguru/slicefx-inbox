using System.Text;
using Inbox.Contracts;
using Inbox.Server.Features.Feeds;
using Inbox.Server.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using SliceFx.Wasi;
using SliceFx.Wasi.HttpClient;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Features.Items;

[Feature("POST /api/items", Summary = "Save a URL for later reading")]
public static class PostItem
{
    public static async Task<SliceResult<PostItemResponse>> Handle(
        PostItemRequest req,
        [FromHeader(Name = "X-Workspace-Token")] string? token,
        IAuthenticator auth,
        IWasiHttpClient http,
        IKeyValueStore kv,
        CancellationToken ct)
    {
        var wid = await auth.AuthenticateAsync(token, ct);
        if (wid is null)
            return SliceResult<PostItemResponse>.Unauthorized();

        // Reject non-https URLs. [Url] DataAnnotation accepts http/ftp; we tighten here
        // because allowed_outbound_hosts is https-only (http URLs would always fail the OG fetch).
        if (!req.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return SliceResult<PostItemResponse>.BadRequest("URL must use the https:// scheme.");

        // Per-workspace item cap — key-count only (full-store scan; accepted at dogfood scale).
        // References RefreshFeeds.MaxItemsPerWorkspace (2000) as the single source of truth.
        // Same TOCTOU race as the feed cap in AddFeed applies; accepted by design.
        var itemCount = await KvScan.CountItemKeysAsync(kv, wid, ct);
        if (itemCount >= RefreshFeeds.MaxItemsPerWorkspace)
            return SliceResult<PostItemResponse>.Problem(
                429, "Item limit reached",
                $"Workspace may not exceed {RefreshFeeds.MaxItemsPerWorkspace} saved items.");

        // Attempt to fetch og:title / <title> from the target page; fail-open (URL as fallback).
        // Follows https redirects up to 3 hops. UTF-8 decode only (WASI encoding support constraint).
        var title = req.Url;
        string? description = null;
        try
        {
            var resp = await FetchFollowingRedirects(http, req.Url, ct);
            if (resp.Status is >= 200 and < 300
                && resp.Headers.TryGetValue("content-type", out var ctype)
                && ctype.Contains("text/html", StringComparison.OrdinalIgnoreCase))
            {
                var meta = HtmlMetadataParser.Parse(Encoding.UTF8.GetString(resp.Body));
                if (!string.IsNullOrWhiteSpace(meta.Title)) title = meta.Title!;
                description = string.IsNullOrWhiteSpace(meta.Description) ? null : meta.Description;
            }
        }
        catch (WasiHttpException) { /* fail-open: URL remains the title */ }

        var id = Guid.NewGuid().ToString("N");
        var item = new InboxItem(id, req.Url, title, description, ItemStatus.Unread, DateTimeOffset.UtcNow, "bookmark");
        // Single-key write — no index update needed. KvScan.ListItemsAsync derives the listing
        // by prefix-scanning w:{wid}:item:* keys, eliminating the former read-modify-write race.
        await kv.SetJsonAsync(WorkspaceKeys.Item(wid, id), item, InboxJsonContext.Default.InboxItem, ct);

        return SliceResult<PostItemResponse>.Ok(new PostItemResponse(id, req.Url, title, description, item.SavedAt));
    }

    // Follows https-only 3xx redirects up to MaxRedirects hops.
    // Stops on non-redirect response, non-https Location, or hop cap.
    // Location values are validated with Uri.TryCreate before assignment to ensure that
    // SpinWasiHttpClient.SendAsync's `new Uri(req.Url, UriKind.Absolute)` never throws
    // UriFormatException from a remote-controlled malformed Location header.
    private static async ValueTask<WasiResponse> FetchFollowingRedirects(
        IWasiHttpClient http, string url, CancellationToken ct)
    {
        const int MaxRedirects = 3;
        for (var i = 0; i < MaxRedirects; i++)
        {
            var resp = await http.SendAsync(new WasiHttpRequest("GET", url, null, null), ct);
            if (resp.Status is not (>= 301 and <= 308)
                || !resp.Headers.TryGetValue("location", out var location))
                return resp;
            if (location.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                && Uri.TryCreate(location, UriKind.Absolute, out _))
                url = location;
            else if (location.StartsWith('/')
                && Uri.TryCreate($"https://{new Uri(url).Authority}{location}", UriKind.Absolute, out var resolved))
                url = resolved.AbsoluteUri;
            else
                return resp; // malformed or non-https Location — stop following
        }
        return await http.SendAsync(new WasiHttpRequest("GET", url, null, null), ct);
    }
}
