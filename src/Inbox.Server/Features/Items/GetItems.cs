using Inbox.Contracts;
using Microsoft.AspNetCore.Mvc;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Features.Items;

[Feature("GET /api/items", Summary = "List inbox items")]
public static class GetItems
{
    public record Response(InboxItem[] Items, int Total);

    // Filters: q (title/url substring), tag (exact), status (exact).
    // All are optional; use string.IsNullOrEmpty rather than != null because the generated
    // C# client emits empty-string for null args (GenerateCSharpClientCommand.cs:487) and
    // the WASI binder treats "" as Bound rather than Missing (WasiArgumentBinder.cs:140,146).
    public static async Task<Response> Handle(
        [FromQuery] string? q,
        [FromQuery] string? tag,
        [FromQuery] string? status,
        IKeyValueStore kv,
        CancellationToken ct)
    {
        var index = await kv.GetJsonAsync("items:index", InboxJsonContext.Default.StringArray, ct) ?? [];

        var items = new List<InboxItem>(index.Length);
        foreach (var id in index)
        {
            var item = await kv.GetJsonAsync($"item:{id}", InboxJsonContext.Default.InboxItem, ct);
            if (item is not null) items.Add(item);
        }

        IEnumerable<InboxItem> filtered = items;

        if (!string.IsNullOrEmpty(q))
            filtered = filtered.Where(i =>
                i.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                i.Url.Contains(q, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(tag))
            filtered = filtered.Where(i => i.Tags != null && i.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(status))
            filtered = filtered.Where(i => string.Equals(i.Status, status, StringComparison.OrdinalIgnoreCase));

        var result = filtered.ToArray();
        return new Response(result, result.Length);
    }
}
