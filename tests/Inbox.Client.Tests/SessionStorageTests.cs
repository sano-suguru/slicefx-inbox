using Bunit;
using Inbox.Client;
using Microsoft.JSInterop;

namespace Inbox.Client.Tests;

public class SessionStorageTests : BunitContext
{
    [Fact]
    public async Task GetItemAsync_returns_null_when_js_throws()
    {
        JSInterop.SetupVoid("sessionStorage.getItem", _ => true)
                 .SetException(new JSException("private browsing mode"));

        var storage = new SessionStorage(JSInterop.JSRuntime);

        var result = await storage.GetItemAsync("some-key");

        Assert.Null(result);
    }

    [Fact]
    public async Task SetItemAsync_does_not_throw_when_js_throws()
    {
        JSInterop.SetupVoid("sessionStorage.setItem", _ => true)
                 .SetException(new JSException("quota exceeded"));

        var storage = new SessionStorage(JSInterop.JSRuntime);

        // Should complete without throwing
        await storage.SetItemAsync("key", "value");
    }

    [Fact]
    public async Task RemoveItemAsync_does_not_throw_when_js_throws()
    {
        JSInterop.SetupVoid("sessionStorage.removeItem", _ => true)
                 .SetException(new JSException("private browsing mode"));

        var storage = new SessionStorage(JSInterop.JSRuntime);

        // Should complete without throwing
        await storage.RemoveItemAsync("key");
    }

    [Fact]
    public async Task GetItemAsync_returns_value_when_js_succeeds()
    {
        JSInterop.Setup<string?>("sessionStorage.getItem", _ => true)
                 .SetResult("stored-value");

        var storage = new SessionStorage(JSInterop.JSRuntime);

        var result = await storage.GetItemAsync("some-key");

        Assert.Equal("stored-value", result);
    }
}
