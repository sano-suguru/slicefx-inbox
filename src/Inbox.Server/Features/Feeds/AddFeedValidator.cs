using Inbox.Contracts;

namespace Inbox.Server.Features.Feeds;

/// <summary>
/// Rejects non-HTTPS feed URLs. <c>[Url]</c> on <see cref="AddFeedRequest"/> accepts any
/// scheme; this validator tightens the requirement to HTTPS because
/// <c>allowed_outbound_hosts</c> is HTTPS-only and HTTP feed URLs would always fail refresh.
/// </summary>
public sealed class AddFeedValidator : ISliceValidator<AddFeedRequest>
{
    public ValueTask<SliceValidationResult> ValidateAsync(AddFeedRequest value, CancellationToken ct)
    {
        if (value.FeedUrl.Length > 0 && !value.FeedUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return ValueTask.FromResult(SliceValidationResult.Failure("FeedUrl", "Feed URL must use the https:// scheme."));

        return ValueTask.FromResult(SliceValidationResult.Success);
    }
}
