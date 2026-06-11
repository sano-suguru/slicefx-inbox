using Bunit;
using Inbox.Client;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Bunit.TestDoubles;

namespace Inbox.Client.Tests;

public class AuthRedirectTests : BunitContext
{
    // Minimal in-memory storage for holder construction.
    private sealed class FakeStorage : ISessionStorage
    {
        private string? _value;

        public ValueTask<string?> GetItemAsync(string key) => ValueTask.FromResult(_value);

        public ValueTask SetItemAsync(string key, string value)
        {
            _value = value;
            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveItemAsync(string key)
        {
            _value = null;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task HandleUnauthorizedAsync_clears_token()
    {
        var storage = new FakeStorage();
        var holder = new RefreshTokenHolder(storage);
        await holder.SetAsync("some-token");

        var nav = Services.GetRequiredService<NavigationManager>();

        await AuthRedirect.HandleUnauthorizedAsync(holder, nav);

        Assert.Null(holder.Token);
    }

    [Fact]
    public async Task HandleUnauthorizedAsync_navigates_to_login_expired()
    {
        var holder = new RefreshTokenHolder(new FakeStorage());
        var nav = Services.GetRequiredService<NavigationManager>();

        await AuthRedirect.HandleUnauthorizedAsync(holder, nav);

        // bUnit's FakeNavigationManager resolves relative URIs against http://localhost/
        Assert.Equal("http://localhost/login?reason=expired", nav.Uri);
    }
}
