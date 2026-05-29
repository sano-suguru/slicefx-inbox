namespace Inbox.Contracts;

public record InboxItem(
    string Id,
    string Url,
    string Title,
    string? Description,
    string Status,
    DateTimeOffset SavedAt,
    string Source);
