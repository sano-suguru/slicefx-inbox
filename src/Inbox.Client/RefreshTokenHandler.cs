namespace Inbox.Client;

/// <summary>
/// Adds the <c>X-Workspace-Token</c> header to every outgoing request when a workspace token is set.
/// Works with the server-side <c>IAuthenticator</c> / <c>KvAuthenticator</c> keyed-lookup mechanism.
/// </summary>
public sealed class RefreshTokenHandler(RefreshTokenHolder holder) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(holder.Token))
            request.Headers.TryAddWithoutValidation("X-Workspace-Token", holder.Token);

        return base.SendAsync(request, cancellationToken);
    }
}
