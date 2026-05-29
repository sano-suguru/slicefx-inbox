using Inbox.Server.Features.Feeds;
using SliceFx.Wasi.HttpClient;
using SliceFx.Wasi.KeyValue;
using SliceFx.Wasi.Spin;

namespace Inbox.Server.Infrastructure;

/// <summary>
/// Cron handler: on each tick, fetch all subscribed feeds and ingest new items.
/// Reuses <see cref="RefreshFeeds.Handle"/> — the same logic invoked via POST /api/feeds/refresh.
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
        // Cron is server-side trusted — call the core method directly, skipping HTTP auth.
        var result = await RefreshFeeds.RefreshAllAsync(_http, _kv, ct);
        Console.Error.WriteLine(
            $"[CronTick] Done: FeedsChecked={result.FeedsChecked} ItemsAdded={result.ItemsAdded} " +
            $"Skipped={result.Skipped} Failed={result.Failed}");
    }
}
