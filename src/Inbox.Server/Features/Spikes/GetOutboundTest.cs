namespace Inbox.Server.Features.Spikes;

/// <summary>
/// Spike 1: Verify outbound HTTP works via wasi:http/outgoing-handler.
/// Hit GET /api/spike/outbound and check that Title is not "unreachable".
/// If Title == "unreachable", the HttpClient outbound path is broken and
/// SliceFx.Wasi.HttpClient satellite needs to be created.
/// Remove this feature once spike 1 is confirmed on Fermyon Cloud.
/// </summary>
[Feature("GET /api/spike/outbound", Summary = "Spike 1: outbound HTTP verification")]
public static class GetOutboundTest
{
    public record Response(string Status, string Title, string? Error);

    public static async Task<Response> Handle(HttpClient http, CancellationToken ct)
    {
        try
        {
            http.Timeout = TimeSpan.FromSeconds(10);
            var html = await http.GetStringAsync("https://example.com", ct);
            var title = ExtractTitle(html) ?? "no-title";
            return new Response("ok", title, null);
        }
        catch (Exception ex)
        {
            return new Response("unreachable", "unreachable", ex.Message);
        }
    }

    private static string? ExtractTitle(string html)
    {
        var start = html.IndexOf("<title", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        start = html.IndexOf('>', start);
        if (start < 0) return null;
        var end = html.IndexOf("</title>", start + 1, StringComparison.OrdinalIgnoreCase);
        if (end < 0) return null;
        return html[(start + 1)..end].Trim();
    }
}
