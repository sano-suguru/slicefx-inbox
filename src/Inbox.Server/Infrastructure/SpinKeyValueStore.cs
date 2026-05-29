// WASI-only: uses WIT-generated types from fermyon:spin/key-value@2.0.0.
// Excluded from non-WASI builds via csproj <Compile Remove> condition.
using SliceFx.Wasi.KeyValue;
using IKeyValue = ProxyWorld.wit.imports.fermyon.spin.v2_0_0.IKeyValue;

namespace Inbox.Server.Infrastructure;

internal sealed class SpinKeyValueStore : IKeyValueStore, IDisposable
{
    private readonly IKeyValue.Store _store;

    public SpinKeyValueStore(string label = "default")
    {
        _store = IKeyValue.Store.Open(label);
    }

    public ValueTask<byte[]?> GetBytesAsync(string key, CancellationToken ct = default)
        => ValueTask.FromResult(_store.Get(key));

    public ValueTask SetBytesAsync(string key, ReadOnlyMemory<byte> value, CancellationToken ct = default)
    {
        _store.Set(key, value.ToArray());
        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteAsync(string key, CancellationToken ct = default)
    {
        _store.Delete(key);
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> ExistsAsync(string key, CancellationToken ct = default)
        => ValueTask.FromResult(_store.Exists(key));

    public ValueTask<IReadOnlyList<string>> ListKeysAsync(CancellationToken ct = default)
        => ValueTask.FromResult<IReadOnlyList<string>>(_store.GetKeys());

    public void Dispose() => _store.Dispose();
}
