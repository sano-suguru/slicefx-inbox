using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Inbox.Contracts;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Features.Items;

[Feature("POST /api/items", Summary = "Save a URL for later reading")]
public static class PostItem
{
    public record Request([Required, Url] string Url);

    public record Response(string Id, string Url, string Title, string? Description, DateTimeOffset SavedAt);

    public static async Task<Response> Handle(Request req, IKeyValueStore kv, IHttpClientFactory httpFactory, CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString("N");
        var (title, description) = await FetchMetaAsync(httpFactory, req.Url, ct);

        var item = new InboxItem(id, req.Url, title, description, "unread", DateTimeOffset.UtcNow, "bookmark");
        await kv.SetJsonAsync($"item:{id}", item, InboxJsonContext.Default.InboxItem, ct);

        var index = await kv.GetJsonAsync("items:index", InboxJsonContext.Default.StringArray, ct) ?? [];
        await kv.SetJsonAsync("items:index", [..index, id], InboxJsonContext.Default.StringArray, ct);

        return new Response(id, req.Url, title, description, item.SavedAt);
    }

    private static async Task<(string Title, string? Description)> FetchMetaAsync(
        IHttpClientFactory httpFactory, string url, CancellationToken ct)
    {
        try
        {
            using var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(10);
            var html = await http.GetStringAsync(url, ct);
            return (ExtractTitle(html) ?? url, ExtractDescription(html));
        }
        catch
        {
            // If outbound HTTP fails (spike 1 not yet verified), fall back to URL as title
            return (url, null);
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
        return HtmlDecode(html[(start + 1)..end].Trim());
    }

    private static string? ExtractDescription(string html)
    {
        var match = Regex.Match(html,
            """<meta\s[^>]*name\s*=\s*["']description["'][^>]*content\s*=\s*["']([^"']+)["']""",
            RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            match = Regex.Match(html,
                """<meta\s[^>]*content\s*=\s*["']([^"']+)["'][^>]*name\s*=\s*["']description["']""",
                RegexOptions.IgnoreCase);
        }
        return match.Success ? HtmlDecode(match.Groups[1].Value.Trim()) : null;
    }

    private static string HtmlDecode(string s) =>
        s.Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
         .Replace("&quot;", "\"").Replace("&#39;", "'").Replace("&apos;", "'");
}
