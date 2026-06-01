namespace Inbox.Server.Infrastructure;

/// <summary>
/// Centralises KV key construction for per-workspace and auth data.
/// All key formats are defined here to ensure consistency across handlers.
/// </summary>
internal static class WorkspaceKeys
{
    // ── Auth ──────────────────────────────────────────────────────────────────
    /// <summary><c>token:{token}</c> → wid (string). O(1) reverse lookup.</summary>
    public static string Token(string token) => $"token:{token}";

    // ── Workspace meta ─────────────────────────────────────────────────────────
    /// <summary><c>workspace:{wid}</c> → <see cref="Workspace"/> JSON.</summary>
    public static string Workspace(string wid) => $"workspace:{wid}";

    /// <summary><c>workspaces:index</c> → string[] of all wids. Read by cron/admin.</summary>
    public const string WorkspacesIndex = "workspaces:index";

    // ── Per-workspace items ────────────────────────────────────────────────────
    /// <summary><c>w:{wid}:item:{id}</c> → <see cref="InboxItem"/> JSON.</summary>
    public static string Item(string wid, string id) => $"w:{wid}:item:{id}";

    /// <summary><c>w:{wid}:items:index</c> → string[] of item IDs (insertion order).</summary>
    public static string ItemsIndex(string wid) => $"w:{wid}:items:index";

    // ── Per-workspace feeds ────────────────────────────────────────────────────
    /// <summary><c>w:{wid}:feed:{id}</c> → <see cref="FeedSubscription"/> JSON.</summary>
    public static string Feed(string wid, string id) => $"w:{wid}:feed:{id}";

    /// <summary><c>w:{wid}:feeds:index</c> → string[] of feed IDs (insertion order).</summary>
    public static string FeedsIndex(string wid) => $"w:{wid}:feeds:index";
}
