using Inbox.Server.Infrastructure;

namespace Inbox.Server.Tests;

public class HtmlMetadataParserTests
{
    [Fact]
    public void HtmlMetadataParser_extracts_og_title()
    {
        const string html = """
            <html>
              <head>
                <meta property="og:title" content="OG Page Title">
                <title>Plain Title</title>
              </head>
            </html>
            """;

        var meta = HtmlMetadataParser.Parse(html);

        // og:title takes priority over <title>
        Assert.Equal("OG Page Title", meta.Title);
    }

    [Fact]
    public void HtmlMetadataParser_falls_back_to_title_tag()
    {
        const string html = """
            <html>
              <head>
                <title>Plain Title</title>
              </head>
            </html>
            """;

        var meta = HtmlMetadataParser.Parse(html);

        Assert.Equal("Plain Title", meta.Title);
    }

    [Fact]
    public void HtmlMetadataParser_extracts_content_when_attribute_order_is_reversed()
    {
        // content attribute appears before property attribute
        const string html = """
            <html>
              <head>
                <meta content="Reversed OG Title" property="og:title">
              </head>
            </html>
            """;

        var meta = HtmlMetadataParser.Parse(html);

        Assert.Equal("Reversed OG Title", meta.Title);
    }

    [Fact]
    public void HtmlMetadataParser_decodes_html_entities()
    {
        const string html = """
            <html>
              <head>
                <meta property="og:title" content="AT&amp;T &gt; Rivals">
              </head>
            </html>
            """;

        var meta = HtmlMetadataParser.Parse(html);

        Assert.Equal("AT&T > Rivals", meta.Title);
    }

    [Fact]
    public void HtmlMetadataParser_returns_null_title_when_not_found()
    {
        const string html = """
            <html><head><meta name="author" content="Nobody"></head></html>
            """;

        var meta = HtmlMetadataParser.Parse(html);

        Assert.Null(meta.Title);
    }

    [Fact]
    public void HtmlMetadataParser_extracts_og_description()
    {
        const string html = """
            <html>
              <head>
                <meta property="og:description" content="A great page summary.">
              </head>
            </html>
            """;

        var meta = HtmlMetadataParser.Parse(html);

        Assert.Equal("A great page summary.", meta.Description);
    }

    [Fact]
    public void HtmlMetadataParser_falls_back_to_meta_description()
    {
        const string html = """
            <html>
              <head>
                <meta name="description" content="Standard meta description.">
              </head>
            </html>
            """;

        var meta = HtmlMetadataParser.Parse(html);

        Assert.Equal("Standard meta description.", meta.Description);
    }

    [Fact]
    public void HtmlMetadataParser_og_description_takes_priority_over_meta_description()
    {
        const string html = """
            <html>
              <head>
                <meta name="description" content="Generic description.">
                <meta property="og:description" content="OG description.">
              </head>
            </html>
            """;

        var meta = HtmlMetadataParser.Parse(html);

        Assert.Equal("OG description.", meta.Description);
    }

    [Fact]
    public void HtmlMetadataParser_returns_null_description_when_not_found()
    {
        const string html = "<html><head></head></html>";

        var meta = HtmlMetadataParser.Parse(html);

        Assert.Null(meta.Description);
    }

    [Fact]
    public void HtmlMetadataParser_does_not_throw_on_empty_input()
    {
        var meta = HtmlMetadataParser.Parse("");

        Assert.Null(meta.Title);
        Assert.Null(meta.Description);
    }

    [Fact]
    public void HtmlMetadataParser_does_not_throw_on_malformed_html()
    {
        var meta = HtmlMetadataParser.Parse("<<<<< not valid >>>> html <<");

        // No exception; both fields null
        Assert.Null(meta.Title);
        Assert.Null(meta.Description);
    }

    [Fact]
    public void HtmlMetadataParser_trims_whitespace_from_extracted_values()
    {
        const string html = """
            <html>
              <head>
                <title>  Padded Title  </title>
              </head>
            </html>
            """;

        var meta = HtmlMetadataParser.Parse(html);

        Assert.Equal("Padded Title", meta.Title);
    }
}
