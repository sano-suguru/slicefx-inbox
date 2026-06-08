using System.Text;
using Inbox.Contracts;

namespace Inbox.Server.Infrastructure;

/// <summary>
/// Builds server-generated HTML pages for public-facing surfaces (share pages, 404).
/// <para>
/// <strong>XSS boundary:</strong> All user-controlled data (item title, description, URL, tags)
/// MUST be passed through <see cref="Escape"/> before embedding in HTML output.
/// This class is the sole location responsible for that escaping.
/// </para>
/// </summary>
internal static class HtmlPage
{
    // ── Inline CSS ─────────────────────────────────────────────────────────────
    // Subset of app.css colour tokens + layout, inlined so share pages do not
    // depend on the Blazor WASM bundle (wwwroot/css/app.css).
    private const string InlineCss = """
        <style>
        :root{
          --bg:#f5f5f5;--surface:#fff;--text:#222;--text-muted:#666;
          --link:#0066cc;--border:#d1d5db;--radius:6px;--shadow:rgba(0,0,0,.1);
          --badge-unread-bg:#dbeafe;--badge-unread-fg:#1e40af;
          --badge-read-bg:#dcfce7;--badge-read-fg:#15803d;
          --badge-archived-bg:#f3f4f6;--badge-archived-fg:#6b7280;
          --badge-tag-bg:#fef3c7;--badge-tag-fg:#92400e;
          --btn-bg:#e5e7eb;--btn-fg:#374151;--btn-hover:#d1d5db;
        }
        @media(prefers-color-scheme:dark){
          :root{
            --bg:#121218;--surface:#1e1e28;--text:#e8e8ea;--text-muted:#9aa0aa;
            --link:#7cb8f0;--border:#3a3a46;--shadow:rgba(0,0,0,.35);
            --badge-unread-bg:#1e3a8a;--badge-unread-fg:#bfdbfe;
            --badge-read-bg:#14532d;--badge-read-fg:#bbf7d0;
            --badge-archived-bg:#374151;--badge-archived-fg:#d1d5db;
            --badge-tag-bg:#78350f;--badge-tag-fg:#fde68a;
            --btn-bg:#2d2d3a;--btn-fg:#d1d5db;--btn-hover:#3a3a4a;
          }
        }
        html{color-scheme:light dark;}
        *,*::before,*::after{box-sizing:border-box;}
        body{font-family:system-ui,-apple-system,sans-serif;margin:0;background:var(--bg);color:var(--text);line-height:1.5;}
        a{color:var(--link);}
        h1{margin-top:0;font-size:1.5em;}
        .container{max-width:860px;margin:0 auto;padding:1.5rem;}
        .card{background:var(--surface);border-radius:8px;padding:1rem 1.25rem;box-shadow:0 1px 3px var(--shadow);}
        .card-url{font-size:.85rem;word-break:break-all;margin-bottom:.5rem;}
        .card-meta{font-size:.8rem;color:var(--text-muted);margin-top:.4rem;}
        .card-desc{margin-top:.6rem;}
        .badge{display:inline-block;font-size:.7rem;font-weight:600;padding:.15rem .45rem;border-radius:999px;margin-right:.25rem;text-transform:uppercase;}
        .badge-unread{background:var(--badge-unread-bg);color:var(--badge-unread-fg);}
        .badge-read{background:var(--badge-read-bg);color:var(--badge-read-fg);}
        .badge-archived{background:var(--badge-archived-bg);color:var(--badge-archived-fg);}
        .badge-tag{background:var(--badge-tag-bg);color:var(--badge-tag-fg);}
        .btn{display:inline-flex;align-items:center;padding:.45rem 1rem;border-radius:var(--radius);background:var(--btn-bg);color:var(--btn-fg);border:none;cursor:pointer;font-size:.9rem;font-weight:500;text-decoration:none;margin-top:1rem;}
        .btn:hover{background:var(--btn-hover);}
        .footer{margin-top:2rem;font-size:.8rem;color:var(--text-muted);}
        </style>
        """;

    // ── Public API ──────────────────────────────────────────────────────────────

    /// <summary>
    /// HTML-encodes a string so it is safe to embed in both text nodes and double-quoted attributes.
    /// Encodes: <c>&amp; &lt; &gt; &quot;</c> (single-quote is not needed when attributes use double-quotes,
    /// but encoded for defence in depth).
    /// Returns an empty string for null/empty input.
    /// </summary>
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        // Fast path: avoid allocations when no encoding is needed.
        var needsEncoding = false;
        foreach (var c in value)
        {
            if (c is '&' or '<' or '>' or '"' or '\'')
            {
                needsEncoding = true;
                break;
            }
        }
        if (!needsEncoding) return value;

        var sb = new StringBuilder(value.Length + 16);
        foreach (var c in value)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\'': sb.Append("&#39;"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Builds the share page HTML for a publicly shared item.
    /// All user-supplied fields are HTML-escaped.
    /// </summary>
    /// <param name="item">The item to render.</param>
    /// <param name="shareToken">The public share token (embedded in og:url).</param>
    /// <param name="baseUrl">Base URL of the deployment (e.g. <c>https://slicefx-inbox-1gat4stw.fermyon.app</c>).</param>
    public static byte[] SharePage(InboxItem item, string shareToken, string baseUrl)
    {
        var title = Escape(item.Title);
        var description = Escape(item.Description);
        var url = Escape(item.Url); // https:// is validated at save time; escape for attribute context
        var source = Escape(item.Source);
        var savedAt = item.SavedAt.ToString("yyyy-MM-dd");
        var shareUrl = Escape($"{baseUrl.TrimEnd('/')}/s/{shareToken}");

        // Status badge CSS class — status values are server-controlled constants, not user input.
        var badgeClass = item.Status switch
        {
            ItemStatus.Read => "badge-read",
            ItemStatus.Archived => "badge-archived",
            _ => "badge-unread",
        };

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.AppendLine($"<title>{title} — SliceFx Inbox</title>");
        sb.AppendLine("<meta name=\"robots\" content=\"index,follow\">");
        // OGP
        sb.AppendLine($"<meta property=\"og:title\" content=\"{title}\">");
        if (!string.IsNullOrEmpty(description))
            sb.AppendLine($"<meta property=\"og:description\" content=\"{description}\">");
        sb.AppendLine($"<meta property=\"og:url\" content=\"{shareUrl}\">");
        sb.AppendLine("<meta property=\"og:type\" content=\"article\">");
        sb.AppendLine("<meta property=\"og:site_name\" content=\"SliceFx Inbox\">");
        // Twitter card
        sb.AppendLine("<meta name=\"twitter:card\" content=\"summary\">");
        sb.AppendLine($"<meta name=\"twitter:title\" content=\"{title}\">");
        if (!string.IsNullOrEmpty(description))
            sb.AppendLine($"<meta name=\"twitter:description\" content=\"{description}\">");
        sb.AppendLine(InlineCss);
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<div class=\"container\">");
        sb.AppendLine($"<h1>{title}</h1>");
        sb.AppendLine("<div class=\"card\">");
        sb.AppendLine($"<div class=\"card-url\"><a href=\"{url}\" target=\"_blank\" rel=\"noopener noreferrer\">{url}</a></div>");
        sb.AppendLine("<div class=\"card-meta\">");
        sb.AppendLine($"<span class=\"badge {badgeClass}\">{Escape(item.Status)}</span>");
        if (item.Tags is { Length: > 0 })
        {
            foreach (var tag in item.Tags)
                sb.AppendLine($"<span class=\"badge badge-tag\">{Escape(tag)}</span>");
        }
        sb.AppendLine($"<span style=\"margin-left:.5rem\">{source} — {savedAt}</span>");
        sb.AppendLine("</div>"); // card-meta
        if (!string.IsNullOrEmpty(description))
            sb.AppendLine($"<div class=\"card-desc\">{description}</div>");
        sb.AppendLine("</div>"); // card
        sb.AppendLine("<div class=\"footer\">Saved with <a href=\"https://github.com/sanosuguru/slicefx-inbox\" rel=\"noopener noreferrer\">SliceFx Inbox</a></div>");
        sb.AppendLine("</div>"); // container
        sb.AppendLine("</body></html>");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>Builds a minimal 404 page for share-not-found responses.</summary>
    public static byte[] NotFound(string message = "This share link is not available.")
    {
        var escaped = Escape(message);
        var html = $"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <title>Not Found — SliceFx Inbox</title>
            <meta name="robots" content="noindex,nofollow">
            {InlineCss}
            </head>
            <body>
            <div class="container">
            <h1>Not Found</h1>
            <p>{escaped}</p>
            </div>
            </body></html>
            """;
        return Encoding.UTF8.GetBytes(html);
    }
}
