using Inbox.Contracts;
using Inbox.Server.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using SliceFx.Wasi.HttpClient;
using SliceFx.Wasi.KeyValue;
using SliceFx.Wasi.Spin;

namespace Inbox.Server.Features.Feeds;

/// <summary>
/// Admin endpoint: refresh feeds for all workspaces.
/// Authenticated via the shared <c>cron_token</c> Spin variable (constant-time comparison).
/// Called by GitHub Actions (prod, every 30 min) and the local Spin cron trigger.
/// </summary>
[Feature("POST /api/feeds/refresh-all", Summary = "Refresh feeds for all workspaces (admin, requires X-Cron-Token)")]
public static class RefreshAllFeeds
{
    public static async Task<SliceResult<RefreshFeedsResponse>> Handle(
        [FromHeader(Name = "X-Cron-Token")] string? cronToken,
        ISpinVariables vars,
        IWasiHttpClient http,
        IKeyValueStore kv,
        CancellationToken ct)
    {
        // Fail-closed: if cron_token variable is unresolvable (null), reject.
        var expected = await vars.GetAsync("cron_token", ct);
        if (!TokenAuth.SafeEquals(cronToken, expected))
            return SliceResult<RefreshFeedsResponse>.Unauthorized();

        var result = await RefreshFeeds.RefreshAllWorkspacesAsync(http, kv, ct);
        return SliceResult<RefreshFeedsResponse>.Ok(result);
    }
}
