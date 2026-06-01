using Inbox.Server.Features.Feeds;
using SliceFx.Wasi.HttpClient;
using SliceFx.Wasi.KeyValue;
using SliceFx.Wasi.Spin;

namespace Inbox.Server.Infrastructure;

/// <summary>
/// Cron handler: on each tick, refresh feeds for all workspaces.
/// Delegates to <see cref="RefreshFeeds.RefreshAllWorkspacesAsync"/> — the same orchestrator
/// invoked via POST /api/feeds/refresh-all.
/// Registered in <see cref="InboxApp"/> and dispatched by <see cref="SpinCronDispatcher"/>.
/// </summary>
internal sealed class FeedRefreshCronHandler : ISpinCronHandler
{
    private readonly IWasiHttpClient _http;
    private readonly IKeyValueStore _kv;

    internal FeedRefreshCronHandler(IWasiHttpClient http, IKeyValueStore kv)
    {
        _http = http;
        _kv = kv;
    }

    public async ValueTask OnTickAsync(SpinCronContext context, CancellationToken ct = default)
    {
        Console.Error.WriteLine($"[CronTick] FireTime={context.FireTime:u}");
        // Cron is server-side trusted — refresh all workspaces directly, skipping HTTP auth.
        var result = await RefreshFeeds.RefreshAllWorkspacesAsync(_http, _kv, ct);
        Console.Error.WriteLine(
            $"[CronTick] Done: FeedsChecked={result.FeedsChecked} ItemsAdded={result.ItemsAdded} " +
            $"Skipped={result.Skipped} Failed={result.Failed}");
    }
}
