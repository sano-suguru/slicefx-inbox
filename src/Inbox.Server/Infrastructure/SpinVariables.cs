// WIT-generated types used here — excluded from non-WASI builds via Inbox.Server.csproj <Compile Remove>.
// VariablesInterop.Get is the free-function entry point; IVariables holds only the Error type.
using VariablesInterop = ProxyWorld.wit.imports.fermyon.spin.v2_0_0.VariablesInterop;

namespace Inbox.Server.Infrastructure;

/// <summary>
/// Reads Spin application variables via fermyon:spin/variables@2.0.0.
/// Fail-closed: any error (undefined, provider, invalid-name) returns null instead of throwing,
/// so callers treat null the same as a wrong token and return 401 rather than 500.
/// </summary>
internal sealed class SpinVariables : ISecrets
{
    // Variable name declared in spin.toml [variables] and mapped to the component.
    private const string RefreshTokenKey = "refresh_token";

    public string? RefreshToken
    {
        get
        {
            try
            {
                // VariablesInterop.Get returns the string value on success,
                // or throws WitException<IVariables.Error> on undefined/provider/invalid-name.
                return VariablesInterop.Get(RefreshTokenKey);
            }
            catch (Exception ex)
            {
                // Fail closed: deny rather than crash.
                Console.Error.WriteLine($"[SpinVariables] Failed to read '{RefreshTokenKey}': {ex.Message}");
                return null;
            }
        }
    }
}
