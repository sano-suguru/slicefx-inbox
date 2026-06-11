using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Bunit;
using Inbox.Client;
using Inbox.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Inbox.Client.Tests;

/// <summary>
/// Minimal in-memory ISessionStorage for test helpers.
/// </summary>
internal sealed class FakeSessionStorage(string? initial = null) : ISessionStorage
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

/// <summary>
/// Captures requests and returns a pre-configured HttpResponseMessage.
/// Used to drive SliceApiClient without a real server.
/// </summary>
internal sealed class StubHttpHandler : HttpMessageHandler
{
    // Queue of responses to return in order; last entry repeats for subsequent calls.
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responders = new();

    public StubHttpHandler RespondWith(HttpResponseMessage response)
    {
        _responders.Enqueue(_ => response);
        return this;
    }

    /// <summary>
    /// Respond with a JSON Problem Details body that triggers SliceApiException.Problem parsing.
    /// </summary>
    public StubHttpHandler RespondWithProblem(HttpStatusCode status, int problemStatus, string title)
    {
        var body = JsonSerializer.Serialize(new { status = problemStatus, title });
        var resp = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/problem+json"),
        };
        _responders.Enqueue(_ => resp);
        return this;
    }

    /// <summary>Build a SliceApiClient that routes through this stub handler with a BaseAddress set.</summary>
    public SliceApiClient BuildClient()
        => new(new HttpClient(this) { BaseAddress = new Uri("http://localhost/") });

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Func<HttpRequestMessage, HttpResponseMessage> responder;
        if (_responders.Count > 1)
            responder = _responders.Dequeue();
        else if (_responders.Count == 1)
            responder = _responders.Peek(); // repeat last entry
        else
            responder = _ => new HttpResponseMessage(HttpStatusCode.OK);

        return Task.FromResult(responder(request));
    }
}

/// <summary>
/// Extension helpers for BunitContext to wire up common Inbox.Client dependencies.
/// </summary>
internal static class BunitContextExtensions
{
    /// <summary>
    /// Register RefreshTokenHolder and ISessionStorage with an optional initial token.
    /// Returns the holder so tests can inspect / mutate Token.
    /// </summary>
    public static RefreshTokenHolder AddTokenHolder(
        this BunitContext ctx, string? initialToken = null)
    {
        var storage = new FakeSessionStorage(initialToken);
        ctx.Services.AddSingleton<ISessionStorage>(storage);
        var holder = new RefreshTokenHolder(storage);
        if (initialToken is not null)
            holder.SetAsync(initialToken).GetAwaiter().GetResult();
        ctx.Services.AddSingleton(holder);
        return holder;
    }
}

/// <summary>
/// Helpers to build canned JSON-body HttpResponseMessages for the stub handler.
/// </summary>
internal static class HttpResponseFactory
{
    public static HttpResponseMessage Json<T>(T value, HttpStatusCode status = HttpStatusCode.OK)
    {
        var json = JsonSerializer.Serialize(value);
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    public static HttpResponseMessage NoContent()
        => new(HttpStatusCode.NoContent);

    public static HttpResponseMessage Unauthorized()
        => new(HttpStatusCode.Unauthorized);
}
