namespace Inbox.Contracts;

public record InboxItem(
    string Id,
    string Url,
    string Title,
    string? Description,
    string Status,
    DateTimeOffset SavedAt,
    string Source);

public record FeedSubscription(
    string Id,
    string FeedUrl,
    string? Title,
    DateTimeOffset AddedAt);
