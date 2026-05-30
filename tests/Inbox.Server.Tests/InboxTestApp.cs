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
/// Mirror of InboxApp.CreateApp() but uses preview.5 in-memory doubles instead of
/// Spin WIT-bound implementations (which are excluded from non-wasi-wasm builds).
/// </summary>
internal static class InboxTestApp
{
    internal static (WasiApp App, InMemoryKeyValueStore Kv, InMemoryWasiHttpClient Http, InMemorySpinVariables Vars)
        Create(string refreshToken = "test-token")
    {
        var builder = WasiHost.CreateBuilder();
        builder.AddSlice();

        builder.Services.AddSingleton(TimeProvider.System);

        var kv = new InMemoryKeyValueStore();
        var http = new InMemoryWasiHttpClient();
        var vars = new InMemorySpinVariables();
        vars.Set("refresh_token", refreshToken);

        builder.Services.AddSingleton<IKeyValueStore>(kv);
        builder.Services.AddSingleton<IWasiHttpClient>(http);
        builder.AddSpinVariables(vars);
        builder.Services.AddSingleton<ITokenGuard>(new RefreshTokenGuard(vars));

        var app = builder.Build();
        return (app, kv, http, vars);
    }

    /// <summary>Dispatch a request with no body and no headers.</summary>
    internal static Task<WasiResponse> GetAsync(
        WasiApp app, string path, string? queryString = null,
        CancellationToken ct = default)
        => app.DispatchAsync(new WasiRequest("GET", path,
            new Dictionary<string, string>(), queryString, null), ct);

    /// <summary>Dispatch an authenticated POST/PATCH/DELETE request with a JSON body.</summary>
    internal static Task<WasiResponse> MutateAsync(
        WasiApp app, string method, string path, byte[]? body = null,
        string token = "test-token", CancellationToken ct = default)
        => app.DispatchAsync(new WasiRequest(method, path,
            new Dictionary<string, string> { ["X-Refresh-Token"] = token },
            null, body), ct);

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
