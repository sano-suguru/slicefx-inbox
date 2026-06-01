using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Infrastructure;

/// <summary>
/// Authenticates a workspace token by looking up <c>token:{token}</c> in KV.
/// O(1) per request — no shared-secret comparison; just a keyed lookup.
/// </summary>
/// <remarks>
/// Fail-closed: a token not present in KV returns null → 401 at the call site.
/// </remarks>
public sealed class KvAuthenticator(IKeyValueStore kv) : IAuthenticator
{
    private readonly IKeyValueStore _kv = kv;

    public async ValueTask<string?> AuthenticateAsync(string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        return await _kv.GetStringAsync(WorkspaceKeys.Token(token), ct);
    }
}
