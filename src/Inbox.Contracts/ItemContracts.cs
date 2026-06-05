namespace Inbox.Contracts;

/// <summary>
/// Well-known identifiers for the shared demo workspace.
/// Defined here (not in Inbox.Server) so both the server and the Blazor WASM
/// client share a single source of truth without any server/WASI dependency.
/// The token is intentionally public — it grants read-write access to shared demo data.
/// </summary>
public static class DemoWorkspace
{
    public const string Token = "demo-access-token";
    public const string Wid = "demo";
}

/// <summary>
/// Valid values for <see cref="InboxItem.Status"/>.
/// Kept here (not in Inbox.Server.Infrastructure) so both the server and
/// the Blazor WASM client share a single source of truth without pulling
/// in any server/WASI dependency.
/// </summary>
public static class ItemStatus
{
    public const string Unread = "unread";
    public const string Read = "read";
    public const string Archived = "archived";
}

public record InboxItem(
    string Id,
    string Url,
    string Title,
    string? Description,
    string Status,
    DateTimeOffset SavedAt,
    string Source,
    string[]? Tags = null);

public record FeedSubscription(
    string Id,
    string FeedUrl,
    string? Title,
    DateTimeOffset AddedAt);

/// <summary>
/// Workspace metadata stored in KV under <c>workspace:{wid}</c>.
/// The token (secret) is stored separately under <c>token:{token}</c> and is never included here.
/// </summary>
/// <remarks>
/// Existing KV blobs may contain an <c>IsDemo</c> field written before this field was removed;
/// System.Text.Json ignores unknown properties by default so deserialization remains compatible.
/// </remarks>
public record Workspace(
    string Id,
    DateTimeOffset CreatedAt);
