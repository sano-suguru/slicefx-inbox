using Inbox.Contracts;
using Microsoft.AspNetCore.Mvc;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Features.Items;

[Feature("GET /api/items", Summary = "List inbox items")]
public static class GetItems
{
    // Filters: q (title/url substring), tag (exact), status (exact).
    // All are optional; use string.IsNullOrEmpty rather than != null (intentional semantics:
    // empty = no filter).
    // Historical note: the generated C# client previously emitted empty-string for null nullable
    // query args ("status=") and the WASI binder treated "" as Bound for string params. Both
    // were fixed upstream in slicefx@de1e953 (issues #3/#4). The IsNullOrEmpty guard is still
    // correct and stays — for string? params empty-string is a valid "no filter" signal.
    public static async Task<GetItemsResponse> Handle(
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
            // Array.Exists + string.Equals avoids MemoryExtensions.Contains which is absent in NativeAOT-LLVM WASI.
            filtered = filtered.Where(i => i.Tags != null && Array.Exists(i.Tags, t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)));

        if (!string.IsNullOrEmpty(status))
            filtered = filtered.Where(i => string.Equals(i.Status, status, StringComparison.OrdinalIgnoreCase));

        var result = filtered.ToArray();
        return new GetItemsResponse(result, result.Length);
    }
}
