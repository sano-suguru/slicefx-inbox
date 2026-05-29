using SliceFx.Wasi;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Features.Items;

[Feature("DELETE /api/items/{id}", Summary = "Remove an inbox item")]
public static class DeleteItem
{
    public static async Task<WasiResponse> Handle(string id, IKeyValueStore kv, CancellationToken ct)
    {
        if (!await kv.ExistsAsync($"item:{id}", ct))
            return SliceResult.Problem(404, "Not Found", $"Item '{id}' not found.");

        await kv.DeleteAsync($"item:{id}", ct);

        var index = await kv.GetJsonAsync("items:index", InboxJsonContext.Default.StringArray, ct) ?? [];
        await kv.SetJsonAsync("items:index", index.Where(x => x != id).ToArray(), InboxJsonContext.Default.StringArray, ct);

        return SliceResult.NoContent();
    }
}
