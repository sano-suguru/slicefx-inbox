using Inbox.Contracts;
using SliceFx.Wasi;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Features.Items;

[Feature("GET /api/items/{id}", Summary = "Get a single inbox item")]
public static class GetItem
{
    public record Response(string Id, string Url, string Title, string? Description, string Status, DateTimeOffset SavedAt, string Source);

    public static async Task<WasiResponse> Handle(string id, IKeyValueStore kv, CancellationToken ct)
    {
        var item = await kv.GetJsonAsync($"item:{id}", InboxJsonContext.Default.InboxItem, ct);
        if (item is null) return SliceResult.Problem(404, "Not Found", $"Item '{id}' not found.");

        return SliceResult.Ok(
            new Response(item.Id, item.Url, item.Title, item.Description, item.Status, item.SavedAt, item.Source),
            InboxJsonContext.Default.GetItemResponse);
    }
}
