using Inbox.Server.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using SliceFx.Wasi;
using SliceFx.Wasi.HttpClient;
using SliceFx.Wasi.KeyValue;
using SliceFx.Wasi.Spin;

namespace Inbox.Server;

// Shared WasiApp — single DI container for both the HTTP trigger (IncomingHandlerImpl)
// and the cron trigger (CronHandlerBridge / ProxyWorldImpl). Built once at module init.
//
// Pre-created instances are used throughout to avoid DI reflection-based constructor
// activation, which NativeAOT full-trim would otherwise strip (same rationale as the
// singleton pattern in the original IncomingHandlerImpl.CreateApp — see comment there).
internal static class InboxApp
{
    internal static readonly WasiApp App = CreateApp();

    private static WasiApp CreateApp()
    {
        var builder = WasiHost.CreateBuilder();
        builder.AddSlice();
        builder.Services.AddSingleton(TimeProvider.System);
        // B1-confirmed: HttpClient.GetStringAsync is not usable in WASI single-thread model.
        // SpinWasiHttpClient + SpinKeyValueStore use synchronous WIT bindings.
        var kv = new SpinKeyValueStore("default");
        var http = new SpinWasiHttpClient();
        builder.Services.AddSingleton<IKeyValueStore>(kv);
        builder.Services.AddSingleton<IWasiHttpClient>(http);
        // Security: read shared token from Spin variables (fermyon:spin/variables@2.0.0).
        // Pre-created singleton — mirrors kv/http pattern; avoids AOT reflection-activation.
        // Fail-closed: SpinVariables returns null on error → mutating endpoints return 401.
        builder.Services.AddSingleton<ISecrets>(new SpinVariables());
        // B2: instance overload (pre-created singleton) — mirrors kv/http pattern above.
        // Generic AddSpinCronHandler<T> AOT-safety is separately proven if needed; keep
        // consistent with existing no-reflection-activation policy for now (rubber duck M8).
        builder.Services.AddSingleton<ISpinCronHandler>(new FeedRefreshCronHandler(http, kv));
        return builder.Build();
    }
}
