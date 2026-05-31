// WIT-independent — included in all builds (non-WASI compile-check + WASI publish).
using System.Net;
using System.Text.RegularExpressions;

namespace Inbox.Server.Infrastructure;

internal sealed record HtmlMetadata(string? Title, string? Description);

/// <summary>
/// Extracts og:title / &lt;title&gt; and og:description from HTML.
/// Best-effort — never throws. Non-UTF-8 pages may produce garbled results;
/// the caller is responsible for decoding the byte body before calling <see cref="Parse"/>.
/// </summary>
internal static class HtmlMetadataParser
{
    // Timeouts guard against pathological inputs (SpinWasiHttpClient caps bodies at 8 MB).
    private static readonly TimeSpan TagTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan AttrTimeout = TimeSpan.FromMilliseconds(50);

    // Matches a complete <meta ...> tag; [^>]{0,2048} is bounded so it cannot span past a '>'.
    private static readonly Regex MetaTagRx = new(
        @"<meta\b[^>]{0,2048}>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline, TagTimeout);

    // Matches <title>...</title>; inner content capped at 1024 chars.
    private static readonly Regex TitleTagRx = new(
        @"<title\b[^>]{0,100}>([\s\S]{0,1024}?)</title>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline, TagTimeout);

    // Attribute selectors — applied to the small meta-tag string, not the full HTML body.
    private static readonly Regex OgTitlePropRx =
        new(@"\bproperty\s*=\s*[""']og:title[""']", RegexOptions.IgnoreCase, AttrTimeout);

    private static readonly Regex OgDescPropRx =
        new(@"\bproperty\s*=\s*[""']og:description[""']", RegexOptions.IgnoreCase, AttrTimeout);

    private static readonly Regex MetaDescNameRx =
        new(@"\bname\s*=\s*[""']description[""']", RegexOptions.IgnoreCase, AttrTimeout);

    private static readonly Regex ContentAttrRx =
        new(@"\bcontent\s*=\s*[""']([^""']{0,1024})[""']", RegexOptions.IgnoreCase, AttrTimeout);

    internal static HtmlMetadata Parse(string html)
    {
        try
        {
            return new HtmlMetadata(ExtractTitle(html), ExtractDescription(html));
        }
        catch (Exception)
        {
            return new HtmlMetadata(null, null);
        }
    }

    private static string? ExtractTitle(string html)
    {
        // Priority 1: og:title
        var ogTitle = FindMetaContent(html, OgTitlePropRx);
        if (ogTitle is not null) return ogTitle;

        // Priority 2: <title>...</title>
        try
        {
            var m = TitleTagRx.Match(html);
            if (m.Success)
            {
                var decoded = WebUtility.HtmlDecode(m.Groups[1].Value).Trim();
                if (decoded.Length > 0) return decoded;
            }
        }
        catch (RegexMatchTimeoutException) { }

        return null;
    }

    private static string? ExtractDescription(string html)
    {
        // Priority 1: og:description
        var ogDesc = FindMetaContent(html, OgDescPropRx);
        if (ogDesc is not null) return ogDesc;

        // Priority 2: <meta name="description">
        return FindMetaContent(html, MetaDescNameRx);
    }

    /// <summary>
    /// Returns the content attribute value of the first &lt;meta&gt; tag that
    /// matches <paramref name="attributeSelector"/>, or null if none found.
    /// </summary>
    private static string? FindMetaContent(string html, Regex attributeSelector)
    {
        MatchCollection metaTags;
        try
        {
            metaTags = MetaTagRx.Matches(html);
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }

        foreach (Match metaMatch in metaTags)
        {
            var tag = metaMatch.Value;
            try
            {
                if (!attributeSelector.IsMatch(tag)) continue;
                var contentMatch = ContentAttrRx.Match(tag);
                if (!contentMatch.Success) continue;
                var decoded = WebUtility.HtmlDecode(contentMatch.Groups[1].Value).Trim();
                if (decoded.Length > 0) return decoded;
            }
            catch (RegexMatchTimeoutException)
            {
                continue;
            }
        }

        return null;
    }
}
