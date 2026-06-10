using Inbox.Contracts;
using Inbox.Server.Filters;
using Inbox.Server.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Features.Feeds;

[Feature("GET /api/feeds", Summary = "List feed subscriptions for the current workspace")]
[SliceFilter<WorkspaceAuthFilter>]
public static class GetFeeds
{
    public static async Task<SliceResult<GetFeedsResponse>> Handle(
        [FromServices] CurrentWorkspace ws,
        IKeyValueStore kv,
        CancellationToken ct)
    {
        var wid = ws.WorkspaceId;

        var result = await KvScan.ListFeedsAsync(kv, wid, ct);
        return SliceResult<GetFeedsResponse>.Ok(new GetFeedsResponse(result, result.Length));
    }
}
