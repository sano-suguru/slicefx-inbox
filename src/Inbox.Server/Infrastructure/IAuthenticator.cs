namespace Inbox.Server.Infrastructure;

/// <summary>
/// Resolves a workspace ID from a caller-supplied opaque token.
/// </summary>
public interface IAuthenticator
{
    /// <summary>
    /// Looks up the workspace ID for <paramref name="token"/>.
    /// Returns null if the token is missing, empty, or not found in KV.
    /// </summary>
    ValueTask<string?> AuthenticateAsync(string? token, CancellationToken ct = default);
}
