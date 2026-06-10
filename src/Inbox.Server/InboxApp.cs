using Inbox.Server.Filters;
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
        // Factory-lambda registrations before AddSlice() so TryAddScoped inside AddSlice is a no-op.
        // ActivatorUtilities reflection is not used — AOT-safe under full-trim NativeAOT-LLVM WASI.
        // (Phase-0 spike confirmed: factory-lambda scoped registration is trim-safe.)
        builder.Services.AddScoped<CurrentWorkspace>(_ => new CurrentWorkspace());
        builder.Services.AddScoped<WorkspaceAuthFilter>(
            sp => new WorkspaceAuthFilter(sp.GetRequiredService<IAuthenticator>()));
        builder.AddSlice();
        builder.Services.AddSingleton(TimeProvider.System);
        // B1-confirmed: HttpClient.GetStringAsync is not usable in WASI single-thread model.
        // SpinWasiHttpClient + SpinKeyValueStore use synchronous WIT bindings.
        var kv = new SpinKeyValueStore("default");
        var http = new SpinWasiHttpClient();
        builder.AddKeyValueStore(kv);
        builder.AddWasiHttpClient(http);
        // Spin variables: cron_token (admin refresh-all endpoint) + registration_open (kill switch).
        // Pre-created singleton; fail-closed per SpinVariables.GetAsync contract (null on WIT error).
        var variables = new SpinVariables();
        builder.AddSpinVariables(variables);
        // Workspace authentication: keyed KV lookup (token:{token} → wid). O(1) per request.
        builder.Services.AddSingleton<IAuthenticator>(new KvAuthenticator(kv));
        // B2: instance overload (pre-created singleton) — mirrors kv/http pattern above.
        // Generic AddSpinCronHandler<T> AOT-safety is separately proven if needed; keep
        // consistent with existing no-reflection-activation policy for now (rubber duck M8).
        builder.Services.AddSingleton<ISpinCronHandler>(new FeedRefreshCronHandler(http, kv));
        return builder.Build();
    }
}
