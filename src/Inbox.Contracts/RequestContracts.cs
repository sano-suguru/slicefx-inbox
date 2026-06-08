using System.ComponentModel.DataAnnotations;

namespace Inbox.Contracts;

// ────────────────────────────────────────────────────────────────────────────
// Form-backing request records — block-bodied with mutable { get; set; }
// so that Blazor @bind-Value can write through a setter (positional/init
// records are incompatible with two-way binding).
// ────────────────────────────────────────────────────────────────────────────

public record PostItemRequest
{
    [Required, Url, StringLength(2048)]
    public string Url { get; set; } = "";
}

public record AddFeedRequest
{
    [Required, Url, StringLength(2048)]
    public string FeedUrl { get; set; } = "";
}

/// <summary>
/// Partial-update payload for PATCH /api/items/{id}.
/// Both fields are optional; omitted fields keep their current value server-side.
/// </summary>
public record UpdateItemRequest
{
    public string? Status { get; set; }
    public string[]? Tags { get; set; }
}

// ────────────────────────────────────────────────────────────────────────────
// Response DTOs — positional records (read-only; never bound to an EditForm).
// ────────────────────────────────────────────────────────────────────────────

public record PostItemResponse(string Id, string Url, string Title, string? Description, DateTimeOffset SavedAt);

public record GetItemsResponse(InboxItem[] Items, int Total);

public record GetItemResponse(
    string Id,
    string Url,
    string Title,
    string? Description,
    string Status,
    DateTimeOffset SavedAt,
    string Source,
    string[]? Tags);

public record AddFeedResponse(string Id, string FeedUrl, DateTimeOffset AddedAt);

public record GetFeedsResponse(FeedSubscription[] Feeds, int Total);

public record RefreshFeedsResponse(int FeedsChecked, int ItemsAdded, int Skipped, int Failed);

/// <summary>
/// Returned by POST /api/workspaces and POST /api/demo.
/// The token is shown once — the caller must save it; it cannot be recovered.
/// </summary>
public record CreateWorkspaceResponse(string Token);

/// <summary>
/// Returned by POST /api/items/{id}/share.
/// Contains only the opaque share token — the caller composes the full public URL
/// by prepending the deployment base URL (e.g. <c>https://…/s/{ShareToken}</c>).
/// The token itself is not a secret (the share page is public), but must not be logged
/// as it could be used to read the item without authentication.
/// </summary>
public record ShareResponse(string ShareToken);
