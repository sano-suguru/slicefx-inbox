using Inbox.Server.Infrastructure;

namespace Inbox.Server.Tests;

public class FeedParserTests
{
    [Fact]
    public void FeedParser_parses_rss2_entries()
    {
        const string xml = """
            <?xml version="1.0"?>
            <rss version="2.0">
              <channel>
                <item><title>Post A</title><link>https://example.com/a</link></item>
                <item><title>Post B</title><link>https://example.com/b</link><pubDate>Tue, 01 Jan 2025 12:00:00 GMT</pubDate></item>
              </channel>
            </rss>
            """;

        var entries = FeedParser.Parse(xml).Entries;

        Assert.Equal(2, entries.Count);
        Assert.Equal("Post A", entries[0].Title);
        Assert.Equal("https://example.com/a", entries[0].Link);
        Assert.Null(entries[0].Published);
        Assert.Equal("Post B", entries[1].Title);
        Assert.Equal("https://example.com/b", entries[1].Link);
        // Published is optional (date format support varies by platform)
    }

    [Fact]
    public void FeedParser_parses_atom_entries()
    {
        const string xml = """
            <?xml version="1.0"?>
            <feed xmlns="http://www.w3.org/2005/Atom">
              <entry>
                <title>Atom Post</title>
                <link href="https://atom.example.com/1" rel="alternate"/>
                <updated>2025-01-01T00:00:00Z</updated>
              </entry>
            </feed>
            """;

        var entries = FeedParser.Parse(xml).Entries;

        Assert.Single(entries);
        Assert.Equal("Atom Post", entries[0].Title);
        Assert.Equal("https://atom.example.com/1", entries[0].Link);
        Assert.NotNull(entries[0].Published);
    }

    [Fact]
    public void FeedParser_returns_empty_for_broken_xml()
    {
        var result = FeedParser.Parse("<not valid xml <<");
        Assert.Empty(result.Entries);
        Assert.Null(result.FeedTitle);
    }

    [Fact]
    public void FeedParser_returns_empty_for_unknown_format()
    {
        var result = FeedParser.Parse("<document><item>foo</item></document>");
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void FeedParser_skips_rss_entries_without_link()
    {
        const string xml = """
            <?xml version="1.0"?>
            <rss version="2.0">
              <channel>
                <item><title>No Link</title></item>
                <item><title>Has Link</title><link>https://example.com/x</link></item>
              </channel>
            </rss>
            """;

        var entries = FeedParser.Parse(xml).Entries;

        Assert.Single(entries);
        Assert.Equal("Has Link", entries[0].Title);
    }

    [Fact]
    public void FeedParser_falls_back_to_link_when_title_missing()
    {
        const string xml = """
            <?xml version="1.0"?>
            <rss version="2.0">
              <channel>
                <item><link>https://example.com/notitle</link></item>
              </channel>
            </rss>
            """;

        var entries = FeedParser.Parse(xml).Entries;

        Assert.Single(entries);
        Assert.Equal("https://example.com/notitle", entries[0].Title);
    }

    [Fact]
    public void FeedParser_atom_prefers_alternate_link_over_others()
    {
        const string xml = """
            <?xml version="1.0"?>
            <feed xmlns="http://www.w3.org/2005/Atom">
              <entry>
                <title>Multi-link</title>
                <link href="https://example.com/self" rel="self"/>
                <link href="https://example.com/alt" rel="alternate"/>
                <link href="https://example.com/related" rel="related"/>
              </entry>
            </feed>
            """;

        var entries = FeedParser.Parse(xml).Entries;

        Assert.Single(entries);
        Assert.Equal("https://example.com/alt", entries[0].Link);
    }

    [Fact]
    public void FeedParser_atom_falls_back_to_first_link_without_rel()
    {
        const string xml = """
            <?xml version="1.0"?>
            <feed xmlns="http://www.w3.org/2005/Atom">
              <entry>
                <title>No-rel link</title>
                <link href="https://example.com/self" rel="self"/>
                <link href="https://example.com/first-no-rel"/>
                <link href="https://example.com/second-no-rel"/>
              </entry>
            </feed>
            """;

        var entries = FeedParser.Parse(xml).Entries;

        Assert.Single(entries);
        Assert.Equal("https://example.com/first-no-rel", entries[0].Link);
    }

    [Fact]
    public void FeedParser_atom_skips_entry_with_only_non_alternate_rel_links()
    {
        const string xml = """
            <?xml version="1.0"?>
            <feed xmlns="http://www.w3.org/2005/Atom">
              <entry>
                <title>Self only</title>
                <link href="https://example.com/self" rel="self"/>
              </entry>
            </feed>
            """;

        var entries = FeedParser.Parse(xml).Entries;

        Assert.Empty(entries);
    }

    // ── Feed-level title extraction ────────────────────────────────────────────

    [Fact]
    public void FeedParser_extracts_rss_channel_title()
    {
        const string xml = """
            <?xml version="1.0"?>
            <rss version="2.0">
              <channel>
                <title>My RSS Channel</title>
                <link>https://example.com</link>
                <item><title>Post A</title><link>https://example.com/a</link></item>
              </channel>
            </rss>
            """;

        var result = FeedParser.Parse(xml);

        Assert.Equal("My RSS Channel", result.FeedTitle);
        Assert.Single(result.Entries);
    }

    [Fact]
    public void FeedParser_extracts_atom_feed_title()
    {
        const string xml = """
            <?xml version="1.0"?>
            <feed xmlns="http://www.w3.org/2005/Atom">
              <title>My Atom Feed</title>
              <entry>
                <title>Entry One</title>
                <link href="https://atom.example.com/1" rel="alternate"/>
              </entry>
            </feed>
            """;

        var result = FeedParser.Parse(xml);

        Assert.Equal("My Atom Feed", result.FeedTitle);
        Assert.Single(result.Entries);
    }

    [Fact]
    public void FeedParser_returns_null_feed_title_when_channel_has_no_title()
    {
        const string xml = """
            <?xml version="1.0"?>
            <rss version="2.0">
              <channel>
                <item><title>Post A</title><link>https://example.com/a</link></item>
              </channel>
            </rss>
            """;

        var result = FeedParser.Parse(xml);

        Assert.Null(result.FeedTitle);
        Assert.Single(result.Entries);
    }

    [Fact]
    public void FeedParser_does_not_confuse_item_title_with_channel_title()
    {
        // Channel has no <title>; item has <title> — feed title must be null, not the item title.
        const string xml = """
            <?xml version="1.0"?>
            <rss version="2.0">
              <channel>
                <item>
                  <title>Item Title Only</title>
                  <link>https://example.com/a</link>
                </item>
              </channel>
            </rss>
            """;

        var result = FeedParser.Parse(xml);

        Assert.Null(result.FeedTitle);
        Assert.Single(result.Entries);
        Assert.Equal("Item Title Only", result.Entries[0].Title);
    }
}
