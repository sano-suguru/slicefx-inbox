using System.Runtime.CompilerServices;
using System.Text;

namespace Inbox.Server.Infrastructure;

/// <summary>
/// A pre-escaped / trusted HTML fragment. Interpolating an <see cref="HtmlRaw"/>
/// into an <see cref="HtmlBuilder"/> appends it verbatim (not re-escaped).
/// Use ONLY for server-controlled markup — never for user-supplied data.
/// </summary>
internal readonly record struct HtmlRaw(string Value)
{
    /// <summary>An empty fragment (produces no output when interpolated).</summary>
    public static readonly HtmlRaw Empty = new(string.Empty);
}

/// <summary>
/// Custom interpolated string handler: HTML-escapes every interpolated
/// <see langword="string?"/> value; <see cref="HtmlRaw"/> holes pass through verbatim.
/// Literal template text is never altered.
/// <para>
/// There is intentionally no generic <c>AppendFormatted&lt;T&gt;</c> overload.
/// Interpolating any type other than <see langword="string?"/> or <see cref="HtmlRaw"/>
/// is a compile-time error, forcing explicit <c>.ToString()</c> conversion and making
/// "forgot to escape" structurally impossible.
/// </para>
/// </summary>
[InterpolatedStringHandler]
internal readonly ref struct HtmlBuilder
{
    private readonly StringBuilder _sb;

    /// <summary>Called by the compiler before any <c>Append*</c> call.</summary>
    public HtmlBuilder(int literalLength, int formattedCount)
        => _sb = new StringBuilder(literalLength + formattedCount * 16);

    /// <summary>Appends a literal template segment verbatim.</summary>
    public void AppendLiteral(string s) => _sb.Append(s);

    /// <summary>HTML-escapes <paramref name="value"/> before appending.</summary>
    public void AppendFormatted(string? value) => _sb.Append(HtmlPage.Escape(value));

    /// <summary>Appends a trusted <see cref="HtmlRaw"/> fragment verbatim.</summary>
    public void AppendFormatted(HtmlRaw raw) => _sb.Append(raw.Value);

    /// <summary>The fully assembled HTML string.</summary>
    internal string Result => _sb.ToString();
}

/// <summary>
/// Factory methods for consuming an <see cref="HtmlBuilder"/> produced by an
/// interpolated string literal.
/// </summary>
internal static class Html
{
    /// <summary>Returns the HTML as a UTF-8 byte array.</summary>
    public static byte[] Bytes(HtmlBuilder html) => Encoding.UTF8.GetBytes(html.Result);

    /// <summary>
    /// Returns the HTML as a <see cref="HtmlRaw"/> fragment safe to embed
    /// verbatim into an outer template. Values interpolated into
    /// <paramref name="html"/> were already HTML-escaped when the fragment was built.
    /// </summary>
    public static HtmlRaw Fragment(HtmlBuilder html) => new(html.Result);
}
