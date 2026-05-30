using SliceFx.Wasi.Spin;

namespace Inbox.Server.Infrastructure;

/// <summary>
/// <see cref="ITokenGuard"/> implementation that reads the shared bearer token from
/// <c>fermyon:spin/variables@2.0.0</c> (or any other <see cref="ISpinVariables"/> provider)
/// and performs a constant-time comparison via <see cref="TokenAuth.SafeEquals"/>.
/// </summary>
/// <remarks>
/// The variable name and comparison logic are encapsulated here so feature handlers
/// never touch the raw secret value and cannot accidentally skip the constant-time check.
/// Fail-closed: if the variable is undefined or unresolvable, <see cref="ISpinVariables.GetAsync"/>
/// returns null and <see cref="TokenAuth.SafeEquals"/> returns false → 401.
/// </remarks>
internal sealed class RefreshTokenGuard : ITokenGuard
{
    private const string RefreshTokenKey = "refresh_token";

    private readonly ISpinVariables _variables;

    public RefreshTokenGuard(ISpinVariables variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        _variables = variables;
    }

    /// <inheritdoc/>
    public async ValueTask<bool> IsAuthorizedAsync(string? token, CancellationToken ct = default)
    {
        var expected = await _variables.GetAsync(RefreshTokenKey, ct);
        return TokenAuth.SafeEquals(token, expected);
    }
}
