// WIT-generated types used here — excluded from non-WASI builds via Inbox.Server.csproj <Compile Remove>.
using IVariables = ProxyWorld.wit.imports.fermyon.spin.v2_0_0.IVariables;

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
                var result = IVariables.Get(RefreshTokenKey);
                // wit-bindgen surfaces result<string,error> as Ok/Err; unwrap the Ok value.
                return result;
            }
            catch (Exception ex)
            {
                // undefined / provider / invalid-name all arrive as exceptions from the generated binding.
                // Fail closed: deny rather than crash.
                Console.Error.WriteLine($"[SpinVariables] Failed to read '{RefreshTokenKey}': {ex.Message}");
                return null;
            }
        }
    }
}
