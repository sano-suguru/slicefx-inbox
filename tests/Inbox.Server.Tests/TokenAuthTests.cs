// TokenAuth.SafeEquals is tested for its value/null/length contract only.
// Timing (constant-time) is intentionally not asserted: wall-clock measurements are noisy and
// flaky in CI, and the implementation deliberately short-circuits on null/length mismatch
// (see TokenAuth.SafeEquals in ITokenGuard.cs) because tokens are fixed-length in practice. This is a best-effort
// design choice made necessary by System.Security.Cryptography being unavailable in NativeAOT-LLVM
// WASI builds (CryptographicOperations.FixedTimeEquals cannot be used).
using Inbox.Server.Infrastructure;

namespace Inbox.Server.Tests;

public class TokenAuthTests
{
    [Fact]
    public void SafeEquals_returns_true_for_identical_tokens()
    {
        Assert.True(TokenAuth.SafeEquals("test-token", "test-token"));
    }

    [Fact]
    public void SafeEquals_returns_true_for_empty_strings()
    {
        // Zero-length XOR loop edge: both empty → both encode to 0 bytes → diff stays 0.
        Assert.True(TokenAuth.SafeEquals("", ""));
    }

    [Fact]
    public void SafeEquals_returns_false_for_different_tokens()
    {
        Assert.False(TokenAuth.SafeEquals("correct-token", "wrong-token--"));
    }

    [Fact]
    public void SafeEquals_returns_false_when_lengths_differ()
    {
        Assert.False(TokenAuth.SafeEquals("short", "a-much-longer-token"));
    }

    [Fact]
    public void SafeEquals_returns_false_when_supplied_is_null()
    {
        Assert.False(TokenAuth.SafeEquals(null, "expected-token"));
    }

    [Fact]
    public void SafeEquals_returns_false_when_expected_is_null()
    {
        Assert.False(TokenAuth.SafeEquals("supplied-token", null));
    }

    [Fact]
    public void SafeEquals_returns_false_when_both_null()
    {
        Assert.False(TokenAuth.SafeEquals(null, null));
    }
}
