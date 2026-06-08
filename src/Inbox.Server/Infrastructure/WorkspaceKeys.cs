namespace Inbox.Server.Infrastructure;

/// <summary>
/// Centralises KV key construction for per-workspace and auth data.
/// All key formats are defined here to ensure consistency across handlers.
/// </summary>
/// <remarks>
/// Listings (items, feeds, workspaces) are derived from prefix scans via
/// <see cref="KvScan"/> rather than from mutable index keys.  The prefix
/// constants here are used by those scan helpers.
/// </remarks>
internal static class WorkspaceKeys
{
    // ── Auth ──────────────────────────────────────────────────────────────────
    /// <summary><c>token:{token}</c> → wid (string). O(1) reverse lookup.</summary>
    public static string Token(string token) => $"token:{token}";

    // ── Workspace meta ─────────────────────────────────────────────────────────
    /// <summary><c>workspace:{wid}</c> → <see cref="Contracts.Workspace"/> JSON.</summary>
    public static string Workspace(string wid) => $"workspace:{wid}";

    /// <summary>
    /// Key prefix used to enumerate all workspace keys via <see cref="KvScan"/>.
    /// Matches <c>workspace:{wid}</c> keys only — does NOT match the legacy
    /// <c>workspaces:index</c> key (note the difference: <c>workspace:</c> vs
    /// <c>workspaces:</c>).
    /// </summary>
    public const string WorkspacePrefix = "workspace:";

    // ── Per-workspace items ────────────────────────────────────────────────────
    /// <summary><c>w:{wid}:item:{id}</c> → <see cref="Contracts.InboxItem"/> JSON.</summary>
    public static string Item(string wid, string id) => $"w:{wid}:item:{id}";

    /// <summary>
    /// Key prefix used to enumerate all item keys for <paramref name="wid"/> via
    /// <see cref="KvScan"/>.  Matches <c>w:{wid}:item:{id}</c> keys only —
    /// does NOT match the legacy <c>w:{wid}:items:index</c> key
    /// (note <c>:item:</c> vs <c>:items:</c>).
    /// </summary>
    public static string ItemPrefix(string wid) => $"w:{wid}:item:";

    // ── Per-item share ─────────────────────────────────────────────────────────
    /// <summary>
    /// <c>share:{shareToken}</c> → <c>"{wid}:{itemId}"</c> — public reverse-lookup.
    /// Presence of this key makes the share page publicly readable.
    /// Parse using <see cref="ParseShare"/> (splits on first <c>:</c> only).
    /// </summary>
    public static string Share(string shareToken) => $"share:{shareToken}";

    /// <summary>
    /// <c>w:{wid}:share:{id}</c> → shareToken (string) — forward lookup.
    /// Used to implement idempotent create and to find the token on delete/revoke.
    /// </summary>
    /// <remarks>
    /// The key is placed under <c>w:{wid}:share:</c> (not <c>w:{wid}:item:</c>) so that
    /// <see cref="KvScan"/> prefix scans using <see cref="ItemPrefix"/> do NOT accidentally
    /// match this key.  Mixing share keys under the item prefix would cause
    /// <see cref="KvScan.CountItemKeysAsync"/> to double-count them and
    /// <see cref="KvScan.ListItemsAsync"/> to attempt (and fail) deserialising them.
    /// </remarks>
    public static string ItemShare(string wid, string id) => $"w:{wid}:share:{id}";

    /// <summary>
    /// Parses the value stored at <see cref="Share"/>: splits on the first <c>:</c>
    /// so that a Guid-format itemId (which contains <c>-</c> but never <c>:</c>) is safe.
    /// </summary>
    /// <returns><c>(wid, itemId)</c> or <c>null</c> if the value is malformed.</returns>
    public static (string Wid, string ItemId)? ParseShare(string? value)
    {
        if (value is null) return null;
        var sep = value.IndexOf(':');
        if (sep <= 0 || sep == value.Length - 1) return null;
        return (value[..sep], value[(sep + 1)..]);
    }

    // ── Per-workspace feeds ────────────────────────────────────────────────────
    /// <summary><c>w:{wid}:feed:{id}</c> → <see cref="Contracts.FeedSubscription"/> JSON.</summary>
    public static string Feed(string wid, string id) => $"w:{wid}:feed:{id}";

    /// <summary>
    /// Key prefix used to enumerate all feed keys for <paramref name="wid"/> via
    /// <see cref="KvScan"/>.  Matches <c>w:{wid}:feed:{id}</c> keys only —
    /// does NOT match the legacy <c>w:{wid}:feeds:index</c> key
    /// (note <c>:feed:</c> vs <c>:feeds:</c>).
    /// </summary>
    public static string FeedPrefix(string wid) => $"w:{wid}:feed:";
}
