using Inbox.Contracts;
using Inbox.Server.Infrastructure;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Features.Workspaces;

/// <summary>
/// Idempotently seeds the shared demo workspace and returns its access token.
/// The demo token is fixed and public — any visitor receives the same token,
/// which grants read-write access to the shared demo data.
/// </summary>
[Feature("POST /api/demo", Summary = "Get the shared demo workspace token (seeds demo data if needed)")]
public static class EnsureDemo
{
    public static async Task<SliceResult<CreateWorkspaceResponse>> Handle(
        IKeyValueStore kv,
        CancellationToken ct)
    {
        var token = await WorkspaceProvisioner.EnsureDemoAsync(kv, ct);
        return SliceResult<CreateWorkspaceResponse>.Ok(new CreateWorkspaceResponse(token));
    }
}
