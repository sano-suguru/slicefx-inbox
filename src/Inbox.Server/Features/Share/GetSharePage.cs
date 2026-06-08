using Inbox.Server.Infrastructure;
using SliceFx.Wasi;
using SliceFx.Wasi.KeyValue;
using SliceFx.Wasi.Spin;

namespace Inbox.Server.Features.Share;

/// <summary>
/// Public share page — returns OGP + body HTML for a shared item.
/// No authentication: this endpoint is intentionally public and unauthenticated.
/// </summary>
/// <remarks>
/// Returns a <see cref="WasiResponse"/> (raw HTML escape hatch) so the source generator
/// passes it through without JSON serialisation or InboxJsonContext registration.
/// <para>
/// Security: all user-supplied fields are HTML-escaped via <see cref="HtmlPage.Escape"/>.
/// The response body never contains the workspace ID or the internal item ID.
/// Unknown or revoked share tokens always return 404 (fail-closed).
/// </para>
/// </remarks>
[Feature("GET /s/{token}", Summary = "Public share page for a saved item")]
public static class GetSharePage
{
    private const string ContentType = "text/html; charset=utf-8";

    public static async Task<WasiResponse> Handle(
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
            return NotFoundResponse();

        var (wid, itemId) = parsed.Value;

        // Fetch the item. If deleted after share was created, return 404 with no-store.
        var item = await kv.GetJsonAsync(WorkspaceKeys.Item(wid, itemId), InboxJsonContext.Default.InboxItem, ct);
        if (item is null)
            return NotFoundResponse();

        // Read the base URL from Spin variables; fall back to the canonical Fermyon URL.
        var baseUrl = await vars.GetAsync("public_base_url", ct)
                      ?? "https://slicefx-inbox-1gat4stw.fermyon.app";

        var body = HtmlPage.SharePage(item, token, baseUrl);
        return new WasiResponse(200,
            new Dictionary<string, string>
            {
                ["Content-Type"] = ContentType,
                ["Cache-Control"] = "no-store, max-age=0",
            }, body);
    }

    private static WasiResponse NotFoundResponse() =>
        new(404,
            new Dictionary<string, string>
            {
                ["Content-Type"] = ContentType,
                ["Cache-Control"] = "no-store",
            },
            HtmlPage.NotFound());
}
