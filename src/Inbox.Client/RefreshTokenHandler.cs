namespace Inbox.Client;

/// <summary>
/// Adds the <c>X-Refresh-Token</c> header to every outgoing request when a token is set.
/// Mirrors the server-side <c>ITokenGuard</c> / <c>RefreshTokenGuard</c> mechanism exactly
/// — no new server abstraction required.
/// </summary>
public sealed class RefreshTokenHandler(RefreshTokenHolder holder) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(holder.Token))
            request.Headers.TryAddWithoutValidation("X-Refresh-Token", holder.Token);

        return base.SendAsync(request, cancellationToken);
    }
}
