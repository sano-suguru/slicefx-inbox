using System.Text;

namespace Inbox.Server.Infrastructure;

/// <summary>
/// Constant-time token comparison helper.
/// Used by the admin <c>POST /api/feeds/refresh-all</c> endpoint to validate the shared
/// <c>cron_token</c> Spin variable. Not used for per-workspace authentication
/// (which uses the keyed KV lookup in <see cref="KvAuthenticator"/>).
/// </summary>
internal static class TokenAuth
{
    /// <summary>
    /// Compares <paramref name="supplied"/> against <paramref name="expected"/> in constant time
    /// using XOR accumulation over <c>max(len(a), len(b))</c> bytes.
    /// Returns false if either is null or the values differ.
    /// </summary>
    /// <remarks>
    /// System.Security.Cryptography is unavailable in NativeAOT-LLVM WASI builds,
    /// so a manual bit-wise XOR loop is used instead of CryptographicOperations.FixedTimeEquals.
    /// Length difference is XOR'd into the accumulator first so that differing-length tokens
    /// always return false without leaking which input is longer via timing.
    /// Do NOT log either value.
    /// </remarks>
    internal static bool SafeEquals(string? supplied, string? expected)
    {
        if (supplied is null || expected is null) return false;
        var a = Encoding.UTF8.GetBytes(supplied);
        var b = Encoding.UTF8.GetBytes(expected);
        // XOR length difference into accumulator — ensures differing lengths return false
        // without an early return that would reveal which side is shorter via timing.
        var maxLen = Math.Max(a.Length, b.Length);
        int diff = a.Length ^ b.Length;
        for (var i = 0; i < maxLen; i++)
            diff |= (i < a.Length ? a[i] : 0) ^ (i < b.Length ? b[i] : 0);
        return diff == 0;
    }
}
