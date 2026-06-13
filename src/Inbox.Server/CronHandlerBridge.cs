// Cron trigger guest export bridge.
//
// wit-bindgen 0.58.0 (componentize-dotnet 0.8.0-preview) generates Proxy.cs which calls
// ProxyWorldExportsImpl.HandleCronEvent(ICronTypesImports.Metadata) for the world-level function export
// "export handle-cron-event: func(metadata: cron-metadata) -> result<_, cron-error>".
//
// Key observations (updated 2026-06-13, componentize-dotnet 0.8.0 / wit-bindgen 0.58):
// - combined.wit uses sync "func" (not "async func") — this is intentional. Component-model async
//   export encoding was fixed in wit-bindgen 0.58, but C# async-export codegen remains preview
//   quality. We keep sync func deliberately; switching to async func is out of scope.
// - World-level function exports use the "ProxyWorldExportsImpl : IProxyWorldExports" pattern,
//   distinct from interface exports (IncomingHandlerImpl : IIncomingHandlerExports).
// - ICronTypesImports.Metadata.timestamp is ulong (Unix epoch seconds); maps to SpinCronContext.FireTime.
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
public sealed class ProxyWorldExportsImpl : IProxyWorldExports
{
    // Called synchronously by [UnmanagedCallersOnly(EntryPoint = "handle-cron-event")]
    // in generated Proxy.cs. Must block — WASI single-thread model (matches IncomingHandlerImpl).
    public static void HandleCronEvent(
        global::ProxyWorld.wit.Imports.spin.cron.v3_0_0.ICronTypesImports.Metadata metadata)
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
