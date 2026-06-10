using Inbox.Server.Infrastructure;
using SliceFx.Wasi.KeyValue;
using SliceFx.Wasi.Spin;

namespace Inbox.Server.Features.Share;

/// <summary>
/// Public share page — returns OGP + body HTML for a shared item.
/// No authentication: this endpoint is intentionally public and unauthenticated.
/// </summary>
/// <remarks>
/// Security: all user-supplied fields are HTML-escaped via <see cref="HtmlPage.Escape"/>.
/// The response body never contains the workspace ID or the internal item ID.
/// Unknown or revoked share tokens always return 404 (fail-closed).
/// </remarks>
[Feature("GET /s/{token}", Summary = "Public share page for a saved item")]
public static class GetSharePage
{
    private const string HtmlContentType = "text/html; charset=utf-8";

    public static async Task<SliceResult> Handle(
        string token,
        IKeyValueStore kv,
        ISpinVariables vars,
        CancellationToken ct)
    {
        // Resolve the share token → (wid, itemId) via the reverse-lookup key.
        // If the key is absent (token unknown or revoked), return 404.
        var shareValue = await kv.GetStringAsync(WorkspaceKeys.Share(token), ct);
        var parsed = WorkspaceKeys.ParseShare(shareValue);
        if (parsed is null)
            return SliceResult.Bytes(HtmlPage.NotFound(), HtmlContentType, 404);

        var (wid, itemId) = parsed.Value;

        // Fetch the item. If deleted after share was created, return 404.
        var item = await kv.GetJsonAsync(WorkspaceKeys.Item(wid, itemId), InboxJsonContext.Default.InboxItem, ct);
        if (item is null)
            return SliceResult.Bytes(HtmlPage.NotFound(), HtmlContentType, 404);

        // Read the base URL from Spin variables; fall back to the canonical Fermyon URL.
        var baseUrl = await vars.GetAsync("public_base_url", ct)
                      ?? "https://slicefx-inbox-1gat4stw.fermyon.app";

        return SliceResult.Bytes(HtmlPage.SharePage(item, token, baseUrl), HtmlContentType);
    }
}
