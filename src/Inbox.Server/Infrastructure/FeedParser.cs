// WIT-independent — included in all builds (non-WASI compile-check + WASI publish).
using System.Globalization;
using System.Xml.Linq;

namespace Inbox.Server.Infrastructure;

internal sealed record ParsedEntry(string Title, string Link, DateTimeOffset? Published);

internal static class FeedParser
{
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";

    /// <summary>
    /// Parse an RSS 2.0 or Atom feed and return its entries.
    /// Entries with an empty or missing link are silently skipped.
    /// </summary>
    internal static IReadOnlyList<ParsedEntry> Parse(string xml)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (Exception)
        {
            return [];
        }

        var root = doc.Root;
        if (root is null) return [];

        // Atom: <feed xmlns="http://www.w3.org/2005/Atom">
        if (root.Name == Atom + "feed")
            return ParseAtom(root);

        // RSS 2.0: <rss version="2.0"><channel>...</channel></rss>
        var channel = root.Element("channel");
        if (channel is not null)
            return ParseRss(channel);

        return [];
    }

    private static List<ParsedEntry> ParseRss(XElement channel)
    {
        var entries = new List<ParsedEntry>();
        foreach (var item in channel.Elements("item"))
        {
            var link = (string?)item.Element("link");
            if (string.IsNullOrWhiteSpace(link)) continue;

            var title = (string?)item.Element("title") ?? link;
            var pubDate = (string?)item.Element("pubDate");
            entries.Add(new ParsedEntry(title.Trim(), link.Trim(), TryParseDate(pubDate)));
        }
        return entries;
    }

    private static List<ParsedEntry> ParseAtom(XElement feed)
    {
        var entries = new List<ParsedEntry>();
        foreach (var entry in feed.Elements(Atom + "entry"))
        {
            // Prefer rel="alternate", then first <link> without rel.
            var link = entry.Elements(Atom + "link")
                .FirstOrDefault(l =>
                    string.Equals((string?)l.Attribute("rel"), "alternate", StringComparison.OrdinalIgnoreCase))
                ?? entry.Elements(Atom + "link")
                    .FirstOrDefault(l => l.Attribute("rel") is null);

            var href = (string?)link?.Attribute("href");
            if (string.IsNullOrWhiteSpace(href)) continue;

            var title = (string?)entry.Element(Atom + "title") ?? href;
            var dateStr = (string?)entry.Element(Atom + "updated")
                       ?? (string?)entry.Element(Atom + "published");
            entries.Add(new ParsedEntry(title.Trim(), href.Trim(), TryParseDate(dateStr)));
        }
        return entries;
    }

    /// <summary>
    /// Try to parse an RFC822 (RSS pubDate) or ISO-8601 (Atom) date string.
    /// Uses InvariantCulture — consistent with the rest of the codebase (IncomingHandlerImpl).
    /// Returns null on failure; callers should substitute DateTimeOffset.UtcNow.
    /// </summary>
    private static DateTimeOffset? TryParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        // AssumeUniversal treats un-qualified times as UTC; AdjustToUniversal normalises to +00:00.
        if (DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
            return parsed;

        return null;
    }
}
