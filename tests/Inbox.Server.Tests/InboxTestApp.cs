using Inbox.Server.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using SliceFx;
using SliceFx.Wasi;
using SliceFx.Wasi.HttpClient;
using SliceFx.Wasi.KeyValue;
using SliceFx.Wasi.Spin;

namespace Inbox.Server.Tests;

/// <summary>
/// Builds an in-process WasiApp wired with in-memory test doubles.
/// Mirror of InboxApp.CreateApp() using preview in-memory doubles
/// instead of Spin WIT-bound implementations (which are excluded from non-wasi-wasm builds).
/// </summary>
internal static class InboxTestApp
{
    /// <summary>Fixed workspace ID used for the default test workspace.</summary>
    internal const string DefaultWid = "test-wid";

    /// <summary>Fixed token that maps to the default test workspace.</summary>
    internal const string DefaultToken = "test-token";

    /// <summary>Fixed admin cron token for testing POST /api/feeds/refresh-all.</summary>
    internal const string DefaultCronToken = "test-cron";

    internal static (WasiApp App, InMemoryKeyValueStore Kv, InMemoryWasiHttpClient Http, InMemorySpinVariables Vars, string DefaultWid)
        Create()
    {
        var builder = WasiHost.CreateBuilder();
        builder.AddSlice();

        builder.Services.AddSingleton(TimeProvider.System);

        var kv = new InMemoryKeyValueStore();
        var http = new InMemoryWasiHttpClient();
        var vars = new InMemorySpinVariables();
        vars.Set("cron_token", DefaultCronToken);
        // registration_open: set explicitly to "true" (mirrors spin.toml default = "true").
        // CreateWorkspace is fail-closed (null → 403), so tests that need creation must have this set.
        // Tests that want "closed" or "null" behavior should override or omit via a separate builder.
        vars.Set("registration_open", "true");

        builder.Services.AddSingleton<IKeyValueStore>(kv);
        builder.Services.AddSingleton<IWasiHttpClient>(http);
        builder.AddSpinVariables(vars);
        builder.Services.AddSingleton<IAuthenticator>(new KvAuthenticator(kv));

        var app = builder.Build();

        // Seed the default workspace so existing tests using DefaultToken pass transparently.
        // Fixed wid (not Guid) so KV read-back assertions can use compile-time constants.
        SeedWorkspaceAsync(kv).GetAwaiter().GetResult();

        return (app, kv, http, vars, DefaultWid);
    }

    /// <summary>
    /// Seeds a workspace entry in KV with the given token and wid.
    /// Use this in tests that need multiple workspaces (e.g. isolation tests).
    /// </summary>
    internal static async Task SeedWorkspaceAsync(
        InMemoryKeyValueStore kv,
        string token = DefaultToken,
        string wid = DefaultWid)
    {
        await ((IKeyValueStore)kv).SetStringAsync(WorkspaceKeys.Token(token), wid, CancellationToken.None);
        var workspace = new Inbox.Contracts.Workspace(wid, DateTimeOffset.UtcNow);
        await ((IKeyValueStore)kv).SetJsonAsync(
            WorkspaceKeys.Workspace(wid), workspace, Inbox.Server.InboxJsonContext.Default.Workspace, CancellationToken.None);
        // No workspaces:index update — workspace listing is derived from KvScan prefix scans.
    }

    /// <summary>
    /// Dispatch a GET request.
    /// By default sends X-Workspace-Token (GET endpoints now require auth).
    /// Pass <c>token: null</c> to simulate an unauthenticated caller.
    /// </summary>
    internal static Task<WasiResponse> GetAsync(
        WasiApp app, string path, string? queryString = null,
        string? token = DefaultToken,
        CancellationToken ct = default)
        => app.DispatchAsync(new WasiRequest("GET", path,
            token is not null
                ? new Dictionary<string, string> { ["X-Workspace-Token"] = token }
                : [],
            queryString, null), ct);

    /// <summary>
    /// Dispatch an authenticated POST/PATCH/DELETE request with a JSON body.
    /// Header is X-Workspace-Token.
    /// </summary>
    internal static Task<WasiResponse> MutateAsync(
        WasiApp app, string method, string path, byte[]? body = null,
        string token = DefaultToken, CancellationToken ct = default)
        => app.DispatchAsync(new WasiRequest(method, path,
            new Dictionary<string, string> { ["X-Workspace-Token"] = token },
            null, body), ct);

    /// <summary>
    /// Dispatch a request with the X-Cron-Token header (admin refresh-all endpoint).
    /// </summary>
    internal static Task<WasiResponse> AdminRefreshAsync(
        WasiApp app, string cronToken = DefaultCronToken, CancellationToken ct = default)
        => app.DispatchAsync(new WasiRequest("POST", "/api/feeds/refresh-all",
            new Dictionary<string, string> { ["X-Cron-Token"] = cronToken },
            null, null), ct);

    /// <summary>
    /// Serialize a value to JSON bytes using the inbox's source-generated context.
    /// </summary>
    internal static byte[] ToJsonBytes<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
        => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);

    /// <summary>
    /// Deserialize the WasiResponse body using the inbox's source-generated context.
    /// </summary>
    internal static T? FromJsonBody<T>(WasiResponse response, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
        => response.Body.Length == 0
            ? default
            : System.Text.Json.JsonSerializer.Deserialize(response.Body, typeInfo);
}
