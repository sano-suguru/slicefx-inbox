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
    public ValueTask<string?> GetItemAsync(string key) =>
        js.InvokeAsync<string?>("sessionStorage.getItem", key);

    public ValueTask SetItemAsync(string key, string value) =>
        js.InvokeVoidAsync("sessionStorage.setItem", key, value);

    public ValueTask RemoveItemAsync(string key) =>
        js.InvokeVoidAsync("sessionStorage.removeItem", key);
}
