// WIT-generated types used here — excluded from non-WASI builds via Inbox.Server.csproj <Compile Remove>.
// VariablesInterop.Get is the free-function entry point; IVariables holds only the Error type.
using SliceFx.Wasi.Spin;
using VariablesInterop = ProxyWorld.wit.imports.fermyon.spin.v2_0_0.VariablesInterop;

namespace Inbox.Server.Infrastructure;

/// <summary>
/// Reads Spin application variables via fermyon:spin/variables@2.0.0.
/// Implements <see cref="ISpinVariables"/> — the SliceFx.Wasi.Spin platform abstraction.
/// Fail-closed: any error (undefined, provider, invalid-name) returns null instead of throwing,
/// so callers treat null the same as a wrong token and return 401 rather than 500.
/// </summary>
internal sealed class SpinVariables : ISpinVariables
{
    /// <inheritdoc/>
    public ValueTask<string?> GetAsync(string name, CancellationToken ct = default)
    {
        try
        {
            // VariablesInterop.Get returns the string value on success,
            // or throws WitException<IVariables.Error> on undefined/provider/invalid-name.
            return ValueTask.FromResult<string?>(VariablesInterop.Get(name));
        }
        catch (Exception ex)
        {
            // Fail closed: deny rather than crash. Matches ISpinVariables contract: undefined → null.
            Console.Error.WriteLine($"[SpinVariables] Failed to read '{name}': {ex.Message}");
            return ValueTask.FromResult<string?>(null);
        }
    }
}
