// WIT-independent — included in all builds (non-WASI compile-check + WASI publish).
using System.Globalization;
using System.Text;
using System.Xml;

namespace Inbox.Server.Infrastructure;

internal sealed record ParsedEntry(string Title, string Link, DateTimeOffset? Published);

/// <summary>
/// Result of parsing a feed: the optional channel/feed-level title plus its entries.
/// <see cref="FeedTitle"/> is null when the feed has no top-level title element or
/// its content is blank.
/// </summary>
internal sealed record ParsedFeed(string? FeedTitle, IReadOnlyList<ParsedEntry> Entries);

internal static class FeedParser
{
    private const string AtomNs = "http://www.w3.org/2005/Atom";

    /// <summary>
    /// Parse an RSS 2.0 or Atom feed and return the feed-level title plus its entries.
    /// Entries with an empty or missing link are silently skipped.
    /// On parse failure returns a <see cref="ParsedFeed"/> with null title and empty entries.
    /// </summary>
    internal static ParsedFeed Parse(string xml)
    {
        try
        {
            // DtdProcessing.Prohibit + null XmlResolver: blocks external entities and DTD fetches.
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
            };
            using var reader = XmlReader.Create(new System.IO.StringReader(xml), settings);

            // Advance past the XML declaration / processing instructions to the root element.
            while (reader.Read() && reader.NodeType != XmlNodeType.Element) { }

            if (!reader.IsStartElement()) return new ParsedFeed(null, []);

            if (reader.NamespaceURI == AtomNs && reader.LocalName == "feed")
                return ParseAtom(reader);

            if (reader.LocalName == "rss" || reader.LocalName == "feed")
                return ParseRss(reader);

            return new ParsedFeed(null, []);
        }
        catch (XmlException)
        {
            return new ParsedFeed(null, []);
        }
    }

    private static ParsedFeed ParseRss(XmlReader reader)
    {
        string? channelTitle = null;
        var entries = new List<ParsedEntry>();
        // Accumulate Text+CDATA nodes across a single element's content.
        var titleBuf = new StringBuilder();

        // Navigate into <channel>
        if (!ReadToDescendant(reader, "", "channel")) return new ParsedFeed(null, entries);

        // Walk <channel> children, capturing the channel-level <title> and all <item> elements.
        // The channel-level <title> appears at channelDepth+1 with no namespace.
        // <item> sub-elements (including their own <title>) are consumed by ReadRssItem.
        var channelDepth = reader.Depth;
        string? currentElem = null;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth <= channelDepth) break;

            switch (reader.NodeType)
            {
                case XmlNodeType.Element when reader.NamespaceURI == "" && reader.LocalName == "item":
                    currentElem = null;
                    var entry = ReadRssItem(reader);
                    if (entry is not null) entries.Add(entry);
                    break;

                case XmlNodeType.Element when reader.NamespaceURI == "" && reader.Depth == channelDepth + 1:
                    // Direct child of <channel> (e.g. <title>, <link>, <description>).
                    currentElem = reader.IsEmptyElement ? null : reader.LocalName;
                    if (currentElem == "title") titleBuf.Clear();
                    break;

                case XmlNodeType.Text or XmlNodeType.CDATA when currentElem == "title":
                    titleBuf.Append(reader.Value);
                    break;

                case XmlNodeType.EndElement:
                    if (currentElem == "title")
                    {
                        var s = titleBuf.ToString().Trim();
                        if (s.Length > 0) channelTitle ??= s;
                    }
                    currentElem = null;
                    break;
            }
        }

        return new ParsedFeed(string.IsNullOrWhiteSpace(channelTitle) ? null : channelTitle, entries);
    }

    private static ParsedEntry? ReadRssItem(XmlReader reader)
    {
        string? link = null, title = null, pubDate = null;
        var depth = reader.Depth;
        // Track which element we are currently inside so we can capture its text.
        string? current = null;
        // Accumulate Text+CDATA nodes; reset when entering a new non-namespaced element.
        var buf = new StringBuilder();

        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    if (reader.NamespaceURI == "")
                    {
                        current = reader.IsEmptyElement ? null : reader.LocalName;
                        buf.Clear();
                    }
                    break;

                case XmlNodeType.Text or XmlNodeType.CDATA when current is "link" or "title" or "pubDate":
                    buf.Append(reader.Value);
                    break;

                case XmlNodeType.EndElement:
                    if (reader.Depth <= depth) goto done;
                    {
                        var s = buf.ToString().Trim();
                        switch (current)
                        {
                            case "link" when s.Length > 0: link ??= s; break;
                            case "title" when s.Length > 0: title ??= s; break;
                            case "pubDate" when s.Length > 0: pubDate ??= s; break;
                        }
                    }
                    current = null;
                    buf.Clear();
                    break;
            }
        }

    done:
        if (string.IsNullOrWhiteSpace(link)) return null;
        return new ParsedEntry((title ?? link).Trim(), link.Trim(), TryParseDate(pubDate));
    }

    private static ParsedFeed ParseAtom(XmlReader reader)
    {
        string? feedTitle = null;
        var entries = new List<ParsedEntry>();
        var depth = reader.Depth; // depth of <feed>
        string? currentElem = null;
        var feedTitleBuf = new StringBuilder();

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth <= depth) break;

            switch (reader.NodeType)
            {
                case XmlNodeType.Element when reader.NamespaceURI == AtomNs && reader.LocalName == "entry":
                    currentElem = null;
                    var entry = ReadAtomEntry(reader);
                    if (entry is not null) entries.Add(entry);
                    break;

                case XmlNodeType.Element when reader.NamespaceURI == AtomNs && reader.Depth == depth + 1:
                    // Direct child of <feed> in the Atom namespace (e.g. <title>, <subtitle>, <updated>).
                    currentElem = reader.IsEmptyElement ? null : reader.LocalName;
                    if (currentElem == "title") feedTitleBuf.Clear();
                    break;

                case XmlNodeType.Element:
                    // Non-Atom element or deeper nesting — reset to avoid false text captures.
                    if (reader.Depth == depth + 1) currentElem = null;
                    break;

                case XmlNodeType.Text or XmlNodeType.CDATA when currentElem == "title":
                    feedTitleBuf.Append(reader.Value);
                    break;

                case XmlNodeType.EndElement:
                    if (currentElem == "title")
                    {
                        var s = feedTitleBuf.ToString().Trim();
                        if (s.Length > 0) feedTitle ??= s;
                    }
                    currentElem = null;
                    break;
            }
        }

        return new ParsedFeed(string.IsNullOrWhiteSpace(feedTitle) ? null : feedTitle, entries);
    }

    private static ParsedEntry? ReadAtomEntry(XmlReader reader)
    {
        string? title = null, alternateHref = null, firstNoRelHref = null, dateStr = null;
        var depth = reader.Depth;
        string? current = null;
        var buf = new StringBuilder();

        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    if (reader.NamespaceURI == AtomNs)
                    {
                        if (reader.LocalName == "link")
                        {
                            // Prefer rel="alternate"; fall back to first <link> without rel.
                            var rel = reader.GetAttribute("rel");
                            var href = reader.GetAttribute("href");
                            if (string.Equals(rel, "alternate", StringComparison.OrdinalIgnoreCase))
                                alternateHref ??= href;
                            else if (rel is null)
                                firstNoRelHref ??= href;
                            current = null; // <link> is self-closing; no text to capture
                        }
                        else
                        {
                            current = reader.IsEmptyElement ? null : reader.LocalName;
                        }
                    }
                    else
                    {
                        current = null;
                    }
                    buf.Clear();
                    break;

                case XmlNodeType.Text or XmlNodeType.CDATA when current is "title" or "updated" or "published":
                    buf.Append(reader.Value);
                    break;

                case XmlNodeType.EndElement:
                    if (reader.Depth <= depth) goto done;
                    {
                        var s = buf.ToString().Trim();
                        switch (current)
                        {
                            case "title" when s.Length > 0: title ??= s; break;
                            case "updated" when s.Length > 0: dateStr = s; break;
                            case "published" when s.Length > 0: dateStr ??= s; break;
                        }
                    }
                    current = null;
                    buf.Clear();
                    break;
            }
        }

    done:
        var link = alternateHref ?? firstNoRelHref;
        if (string.IsNullOrWhiteSpace(link)) return null;
        return new ParsedEntry((title ?? link).Trim(), link.Trim(), TryParseDate(dateStr));
    }

    private static bool ReadToDescendant(XmlReader reader, string ns, string localName)
    {
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element
                && reader.NamespaceURI == ns
                && reader.LocalName == localName)
                return true;
        }
        return false;
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
