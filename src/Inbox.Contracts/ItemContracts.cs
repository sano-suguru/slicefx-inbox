namespace Inbox.Contracts;

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
