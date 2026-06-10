using Inbox.Contracts;

namespace Inbox.Server.Features.Items;

/// <summary>
/// Rejects non-HTTPS item URLs. <c>[Url]</c> on <see cref="PostItemRequest"/> accepts any
/// scheme; this validator tightens the requirement to HTTPS because
/// <c>allowed_outbound_hosts</c> is HTTPS-only and an HTTP URL would always fail the OG fetch.
/// </summary>
public sealed class PostItemValidator : ISliceValidator<PostItemRequest>
{
    public ValueTask<SliceValidationResult> ValidateAsync(PostItemRequest value, CancellationToken ct)
    {
        if (value.Url.Length > 0 && !value.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return ValueTask.FromResult(SliceValidationResult.Failure("Url", "URL must use the https:// scheme."));

        return ValueTask.FromResult(SliceValidationResult.Success);
    }
}
