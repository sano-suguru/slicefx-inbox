using Inbox.Contracts;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Features.Items;

[Feature("GET /api/items", Summary = "List inbox items")]
public static class GetItems
{
    public record Response(InboxItem[] Items, int Total);

    public static async Task<Response> Handle(IKeyValueStore kv, CancellationToken ct)
    {
        var index = await kv.GetJsonAsync("items:index", InboxJsonContext.Default.StringArray, ct) ?? [];

        var items = new List<InboxItem>(index.Length);
        foreach (var id in index)
        {
            var item = await kv.GetJsonAsync($"item:{id}", InboxJsonContext.Default.InboxItem, ct);
            if (item is not null) items.Add(item);
        }

        var result = items.ToArray();
        return new Response(result, result.Length);
    }
}
