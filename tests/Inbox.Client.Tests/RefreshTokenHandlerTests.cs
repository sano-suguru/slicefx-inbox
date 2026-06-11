using System.Net;
using Inbox.Client;

namespace Inbox.Client.Tests;

public class RefreshTokenHandlerTests
{
    // Captures the outgoing request for header inspection.
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private static (HttpClient Client, CapturingHandler Capture) BuildClient(RefreshTokenHolder holder)
    {
        var capture = new CapturingHandler();
        var handler = new RefreshTokenHandler(holder) { InnerHandler = capture };
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        return (client, capture);
    }

    private static RefreshTokenHolder HolderWithToken(string? token)
    {
        // Directly construct holder using a fake storage and bypass InitializeAsync
        // by relying on SetAsync so the in-memory Token field is populated.
        var storage = new InMemorySessionStorage(token);
        var holder = new RefreshTokenHolder(storage);
        // Use GetAwaiter to avoid async overhead in test setup; CancellationToken.None is intentional.
        if (token is not null)
            holder.SetAsync(token).GetAwaiter().GetResult();
        return holder;
    }

    // Minimal in-memory storage for test setup only.
    private sealed class InMemorySessionStorage(string? initial) : ISessionStorage
    {
        private string? _value = initial;

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
    public async Task SendAsync_adds_header_when_token_is_set()
    {
        var holder = HolderWithToken("my-token");
        var (client, capture) = BuildClient(holder);

        await client.GetAsync("/api/test");

        Assert.NotNull(capture.LastRequest);
        Assert.True(capture.LastRequest!.Headers.TryGetValues("X-Workspace-Token", out var values));
        Assert.Equal("my-token", values.Single());
    }

    [Fact]
    public async Task SendAsync_does_not_add_header_when_token_is_null()
    {
        var holder = new RefreshTokenHolder(new InMemorySessionStorage(null));
        var (client, capture) = BuildClient(holder);

        await client.GetAsync("/api/test");

        Assert.NotNull(capture.LastRequest);
        Assert.False(capture.LastRequest!.Headers.Contains("X-Workspace-Token"));
    }

    [Fact]
    public async Task SendAsync_does_not_add_header_when_token_is_empty()
    {
        // RefreshTokenHolder.SetAsync converts empty/whitespace to null,
        // so Token is null when an empty string is set.
        var holder = HolderWithToken("   ");
        // After SetAsync, Token should be null — verify the assumption.
        Assert.Null(holder.Token);

        var (client, capture) = BuildClient(holder);
        await client.GetAsync("/api/test");

        Assert.NotNull(capture.LastRequest);
        Assert.False(capture.LastRequest!.Headers.Contains("X-Workspace-Token"));
    }
}
