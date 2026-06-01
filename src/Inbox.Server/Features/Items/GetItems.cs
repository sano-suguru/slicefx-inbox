using Inbox.Contracts;
using Inbox.Server.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Features.Items;

[Feature("GET /api/items", Summary = "List inbox items for the current workspace")]
public static class GetItems
{
    // Filters: q (title/url substring), tag (exact), status (exact).
    // All are optional; use string.IsNullOrEmpty rather than != null (intentional semantics:
    // empty = no filter).
    // Historical note: the generated C# client previously emitted empty-string for null nullable
    // query args ("status=") and the WASI binder treated "" as Bound for string params. Both
    // were fixed upstream in slicefx@de1e953 (issues #3/#4). The IsNullOrEmpty guard is still
    // correct and stays — for string? params empty-string is a valid "no filter" signal.
    public static async Task<SliceResult<GetItemsResponse>> Handle(
        [FromHeader(Name = "X-Workspace-Token")] string? token,
        IAuthenticator auth,
        [FromQuery] string? q,
        [FromQuery] string? tag,
        [FromQuery] string? status,
        IKeyValueStore kv,
        CancellationToken ct)
    {
        var wid = await auth.AuthenticateAsync(token, ct);
        if (wid is null)
            return SliceResult<GetItemsResponse>.Unauthorized();

        var index = await kv.GetJsonAsync(WorkspaceKeys.ItemsIndex(wid), InboxJsonContext.Default.StringArray, ct) ?? [];

        var items = new List<InboxItem>(index.Length);
        foreach (var id in index)
        {
            var item = await kv.GetJsonAsync(WorkspaceKeys.Item(wid, id), InboxJsonContext.Default.InboxItem, ct);
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
        return SliceResult<GetItemsResponse>.Ok(new GetItemsResponse(result, result.Length));
    }
}
