namespace Inbox.Server.Infrastructure;

/// <summary>
/// Scoped holder for the resolved workspace identity.
/// Populated by <see cref="Filters.WorkspaceAuthFilter"/> before the handler runs.
/// Registered with a factory lambda in <see cref="InboxApp"/> to avoid
/// <c>ActivatorUtilities</c> reflection under full-trim NativeAOT-LLVM WASI.
/// </summary>
public sealed class CurrentWorkspace
{
    /// <summary>Gets or sets the authenticated workspace identifier.</summary>
    public string WorkspaceId { get; set; } = string.Empty;
}
