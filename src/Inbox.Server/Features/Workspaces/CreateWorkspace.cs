using Inbox.Contracts;
using Inbox.Server.Infrastructure;
using SliceFx.Wasi.KeyValue;
using SliceFx.Wasi.Spin;

namespace Inbox.Server.Features.Workspaces;

[Feature("POST /api/workspaces", Summary = "Create a new anonymous workspace and return its access token (shown once)")]
public static class CreateWorkspace
{
    public static async Task<SliceResult<CreateWorkspaceResponse>> Handle(
        ISpinVariables vars,
        IKeyValueStore kv,
        CancellationToken ct)
    {
        // Kill switch: registration is allowed unless explicitly set to "false".
        // Fail-open: null / WIT error → treat as enabled (matches default = "true").
        var regOpen = await vars.GetAsync("registration_open", ct);
        if (string.Equals(regOpen, "false", StringComparison.OrdinalIgnoreCase))
            return SliceResult<CreateWorkspaceResponse>.Problem(
                403, "Registration closed", "New workspace registration is currently disabled.");

        var result = await WorkspaceProvisioner.CreateAsync(kv, ct);
        if (result is null)
            return SliceResult<CreateWorkspaceResponse>.Problem(
                429, "Workspace limit reached", "The maximum number of workspaces has been reached.");

        return SliceResult<CreateWorkspaceResponse>.Ok(new CreateWorkspaceResponse(result.Value.Token));
    }
}
