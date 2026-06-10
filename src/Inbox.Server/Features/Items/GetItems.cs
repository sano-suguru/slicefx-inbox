using Inbox.Contracts;
using Inbox.Server.Filters;
using Inbox.Server.Infrastructure;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Features.Items;

[Feature("GET /api/items", Summary = "List inbox items for the current workspace")]
[SliceFilter<WorkspaceAuthFilter>]
public static class GetItems
{
    // Filters: q (title/url substring), tag (exact), status (exact).
    // All are optional; use string.IsNullOrEmpty rather than != null (intentional semantics:
    // empty = no filter). For string? params empty-string is a valid "no filter" signal.
    public static async Task<SliceResult<GetItemsResponse>> Handle(
        CurrentWorkspace ws,
        string? q,
        string? tag,
        string? status,
        int? limit,
        int? offset,
        IKeyValueStore kv,
        CancellationToken ct)
    {
        var wid = ws.WorkspaceId;
        var items = await KvScan.ListItemsAsync(kv, wid, ct);

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

        // Newest-first: descending SavedAt, then descending Id as stable tiebreaker.
        // Ordering is intentionally in GetItems rather than KvScan so other callers
        // of ListItemsAsync (cron refresh, etc.) keep their ascending-order contract.
        var sorted = filtered.OrderByDescending(i => i.SavedAt).ThenByDescending(i => i.Id).ToArray();
        var total = sorted.Length;

        // Server-side paging: in-memory slice after full KV scan.
        // Reduces transfer and client DOM cost, but KV read cost remains O(total) —
        // see KvScan performance note. True scale fix requires an index or DB.
        var pageOffset = Math.Max(0, offset ?? 0);
        var pageLimit = Math.Clamp(limit ?? 50, 1, 200);
        var result = sorted.Skip(pageOffset).Take(pageLimit).ToArray();

        return SliceResult<GetItemsResponse>.Ok(new GetItemsResponse(result, total));
    }
}
