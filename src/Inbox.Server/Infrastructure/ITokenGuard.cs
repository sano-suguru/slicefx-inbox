using System.Text;

namespace Inbox.Server.Infrastructure;

/// <summary>
/// Encapsulates bearer-token authorization for mutating endpoints.
/// Implementations read the expected token from a secrets store and compare
/// against the caller-supplied value in constant time.
/// </summary>
public interface ITokenGuard
{
    /// <summary>
    /// Returns <c>true</c> if <paramref name="token"/> matches the expected shared secret,
    /// <c>false</c> for any mismatch (wrong value, null, or unresolvable secret).
    /// </summary>
    ValueTask<bool> IsAuthorizedAsync(string? token, CancellationToken ct = default);
}

/// <summary>
/// Constant-time token comparison helper.
/// </summary>
internal static class TokenAuth
{
    /// <summary>
    /// Compares <paramref name="supplied"/> against <paramref name="expected"/> in constant time
    /// using XOR accumulation. Returns false if either is null or the values differ.
    /// </summary>
    /// <remarks>
    /// System.Security.Cryptography is unavailable in NativeAOT-LLVM WASI builds,
    /// so a manual bit-wise XOR loop is used instead of CryptographicOperations.FixedTimeEquals.
    /// Length comparison short-circuits, but tokens are fixed-length in practice so this is fine.
    /// Do NOT log either value.
    /// </remarks>
    internal static bool SafeEquals(string? supplied, string? expected)
    {
        if (supplied is null || expected is null) return false;
        var a = Encoding.UTF8.GetBytes(supplied);
        var b = Encoding.UTF8.GetBytes(expected);
        if (a.Length != b.Length) return false;
        int diff = 0;
        for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
