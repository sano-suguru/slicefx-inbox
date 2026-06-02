// Cron trigger guest export bridge.
//
// wit-bindgen 0.41.0 (componentize-dotnet 0.7.0-preview) generates Proxy.cs which calls
// ProxyWorldImpl.HandleCronEvent(ICronTypes.Metadata) for the world-level function export
// "export handle-cron-event: async func(metadata: cron-metadata) -> result<_, cron-error>".
//
// Key observations (B2 spike, 2026-05-29):
// - "async func" in WIT maps to a synchronous C# void method + [async] ABI entry-point name.
//   The component model async convention is expressed in the WASM binary ([async]handle-cron-event),
//   but the C# guest binding is plain synchronous — no Task/ValueTask.
// - World-level function exports use the "ProxyWorldImpl" pattern, distinct from interface
//   exports (IncomingHandlerImpl : IIncomingHandler). ProxyWorldImpl is looked up by name.
// - ICronTypes.Metadata.timestamp is ulong (Unix epoch seconds); maps to SpinCronContext.FireTime.
// - SpinCronContext.Metadata has no WIT source in spin:cron@3.0.0 (only timestamp is provided);
//   it will always be null on this path (recorded as abstraction observation for preview.5).
// CA1707/CA1711: WIT-bindgen generates versioned namespaces and type names containing underscores;
// this bridge must inherit the ProxyWorld namespace to satisfy the generated partial contracts.
#pragma warning disable CA1707, CA1711
using SliceFx.Wasi.Spin;

namespace ProxyWorld;

/// <summary>
/// World-level cron export implementation. Called synchronously by the WASM runtime entry point
/// generated in Proxy.cs. Bridges to <see cref="SpinCronDispatcher"/> and thence to
/// <see cref="global::Inbox.Server.Infrastructure.FeedRefreshCronHandler"/>.
/// </summary>
public sealed class ProxyWorldImpl : IProxyWorld
{
    // Called synchronously by [UnmanagedCallersOnly(EntryPoint = "[async]handle-cron-event")]
    // in generated Proxy.cs. Must block — WASI single-thread model (matches IncomingHandlerImpl).
    public static void HandleCronEvent(
        global::ProxyWorld.wit.imports.spin.cron.v3_0_0.ICronTypes.Metadata metadata)
    {
        var fireTime = DateTimeOffset.FromUnixTimeSeconds((long)metadata.timestamp);
        // SpinCronContext.Metadata = null here: spin:cron@3.0.0 metadata is timestamp-only.
        var context = new SpinCronContext(fireTime);
        SpinCronDispatcher
            .DispatchAsync(global::Inbox.Server.InboxApp.App, context)
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }
}
#pragma warning restore CA1707, CA1711
