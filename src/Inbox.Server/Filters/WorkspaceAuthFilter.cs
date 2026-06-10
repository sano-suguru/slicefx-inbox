using Inbox.Server.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Inbox.Server.Filters;

/// <summary>
/// Validates <c>X-Workspace-Token</c> and stores the resolved workspace ID in the
/// scoped <see cref="CurrentWorkspace"/> service so handlers can inject it directly.
/// Short-circuits with 401 on missing or unknown tokens (fail-closed).
/// </summary>
public sealed class WorkspaceAuthFilter : ISliceFilter
{
    private readonly IAuthenticator _auth;

    /// <summary>Initializes the filter with the workspace authenticator.</summary>
    public WorkspaceAuthFilter(IAuthenticator auth) => _auth = auth;

    /// <inheritdoc/>
    public async ValueTask<SliceFilterResult> InvokeAsync(SliceFilterContext context, SliceFilterDelegate next)
    {
        context.Headers.TryGetValue("X-Workspace-Token", out var token);

        var wid = await _auth.AuthenticateAsync(token, context.CancellationToken).ConfigureAwait(false);
        if (wid is null)
        {
            return SliceFilterResult.ShortCircuit(SliceResult.Unauthorized());
        }

        var ws = context.Services.GetRequiredService<CurrentWorkspace>();
        ws.WorkspaceId = wid;

        return await next(context).ConfigureAwait(false);
    }
}
