using Microsoft.JSInterop;

namespace Inbox.Client;

/// <summary>
/// Thin wrapper over browser <c>sessionStorage</c> via <see cref="IJSRuntime"/>.
/// Data cleared automatically when the tab is closed (unlike localStorage).
/// </summary>
public interface ISessionStorage
{
    ValueTask<string?> GetItemAsync(string key);
    ValueTask SetItemAsync(string key, string value);
    ValueTask RemoveItemAsync(string key);
}

public sealed class SessionStorage(IJSRuntime js) : ISessionStorage
{
    // try/catch guards: private browsing mode or storage quota exhaustion can throw
    // JSException from sessionStorage interop. Failing silently is preferable to
    // crashing the app — the worst case is that the token isn't persisted and the
    // user must re-enter it (same UX as a fresh session).

    public async ValueTask<string?> GetItemAsync(string key)
    {
        try { return await js.InvokeAsync<string?>("sessionStorage.getItem", key); }
        catch { return null; }
    }

    public async ValueTask SetItemAsync(string key, string value)
    {
        try { await js.InvokeVoidAsync("sessionStorage.setItem", key, value); }
        catch { /* private mode / quota exceeded — no-op */ }
    }

    public async ValueTask RemoveItemAsync(string key)
    {
        try { await js.InvokeVoidAsync("sessionStorage.removeItem", key); }
        catch { /* private mode — no-op */ }
    }
}
