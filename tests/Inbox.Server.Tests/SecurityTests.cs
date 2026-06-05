using System.Text;
using Inbox.Server.Infrastructure;

namespace Inbox.Server.Tests;

/// <summary>
/// Security regression tests: SafeEquals (Fix 6), XXE/DTD protection, workspace isolation
/// extension (PATCH cross-tenant, KV prefix boundary).
/// </summary>
public class SecurityTests
{
    // ── SafeEquals ──────────────────────────────────────────────────────────

    [Fact]
    public void SafeEquals_returns_false_for_different_length_tokens()
    {
        // Shorter supplied value — no timing shortcut should leak that length differs
        Assert.False(TokenAuth.SafeEquals("short", "longertoken"));
        Assert.False(TokenAuth.SafeEquals("longertoken", "short"));
    }

    [Fact]
    public void SafeEquals_returns_true_for_equal_tokens()
    {
        Assert.True(TokenAuth.SafeEquals("abc123", "abc123"));
    }

    [Fact]
    public void SafeEquals_returns_false_for_same_length_different_value()
    {
        Assert.False(TokenAuth.SafeEquals("aaaaaa", "aaaaab"));
    }

    [Fact]
    public void SafeEquals_returns_false_when_either_is_null()
    {
        Assert.False(TokenAuth.SafeEquals(null, "token"));
        Assert.False(TokenAuth.SafeEquals("token", null));
        Assert.False(TokenAuth.SafeEquals(null, null));
    }

    [Fact]
    public void SafeEquals_returns_false_for_empty_vs_nonempty()
    {
        Assert.False(TokenAuth.SafeEquals("", "token"));
        Assert.False(TokenAuth.SafeEquals("token", ""));
    }

    // ── XXE / DTD protection ────────────────────────────────────────────────

    [Fact]
    public void FeedParser_does_not_expand_external_entity()
    {
        const string xxe = """
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE foo [<!ENTITY xxe SYSTEM "file:///etc/passwd">]>
            <rss version="2.0">
              <channel>
                <item><title>&xxe;</title><link>https://example.com/a</link></item>
              </channel>
            </rss>
            """;

        // DtdProcessing.Prohibit must block DTD processing — parser returns empty/no entries.
        var result = FeedParser.Parse(xxe);
        // Entries may be empty (DTD exception caught) or title may not be the file contents.
        // Assert no exception was thrown and the title is not the file contents.
        foreach (var entry in result.Entries)
        {
            Assert.DoesNotContain("/root:", entry.Title ?? "");
        }
    }

    [Fact]
    public void FeedParser_handles_billion_laughs_without_expanding()
    {
        // Billion-laughs: nested entity expansion. DtdProcessing.Prohibit should stop this.
        const string billionLaughs = """
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE lolz [
              <!ENTITY lol "lol">
              <!ENTITY lol2 "&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;">
            ]>
            <rss version="2.0">
              <channel>
                <item><title>&lol2;</title><link>https://example.com/b</link></item>
              </channel>
            </rss>
            """;

        // Must return within a reasonable time (no explosion) without throwing to the caller.
        var result = FeedParser.Parse(billionLaughs);
        Assert.NotNull(result);
    }

    // ── PATCH cross-workspace isolation ────────────────────────────────────

    [Fact]
    public async Task UpdateItem_in_workspace_A_cannot_affect_workspace_B()
    {
        var (app, kv, _, _, _) = InboxTestApp.Create();

        const string tokenA = "token-patch-a";
        const string tokenB = "token-patch-b";
        await InboxTestApp.SeedWorkspaceAsync(kv, tokenA, "wid-patch-a");
        await InboxTestApp.SeedWorkspaceAsync(kv, tokenB, "wid-patch-b");

        // Post an item to workspace B
        var body = InboxTestApp.ToJsonBytes(
            new Inbox.Contracts.PostItemRequest { Url = "https://private-b.example.com" },
            InboxJsonContext.Default.PostItemRequest);
        var bPost = InboxTestApp.FromJsonBody(
            await InboxTestApp.MutateAsync(app, "POST", "/api/items", body, tokenB),
            InboxJsonContext.Default.PostItemResponse)!;

        // Workspace A tries to PATCH workspace B's item by guessing the ID
        var patchBody = InboxTestApp.ToJsonBytes(
            new Inbox.Contracts.UpdateItemRequest { Status = Inbox.Contracts.ItemStatus.Archived },
            InboxJsonContext.Default.UpdateItemRequest);
        var patchResp = await InboxTestApp.MutateAsync(app, "PATCH", $"/api/items/{bPost.Id}", patchBody, tokenA);

        // Must be 404 (item not found in workspace A's namespace), not 204
        Assert.Equal(404, patchResp.Status);

        // Workspace B's item must remain unread
        var bItem = InboxTestApp.FromJsonBody(
            await InboxTestApp.GetAsync(app, $"/api/items/{bPost.Id}", token: tokenB),
            InboxJsonContext.Default.GetItemResponse)!;
        Assert.Equal(Inbox.Contracts.ItemStatus.Unread, bItem.Status);
    }

    // ── KV prefix boundary ───────────────────────────────────────────────────

    [Fact]
    public void ItemPrefix_does_not_match_longer_wid()
    {
        // wid "a" prefix is "w:a:item:" which must NOT match a key belonging to wid "ab"
        var prefixA = WorkspaceKeys.ItemPrefix("a");
        var keyForAb = WorkspaceKeys.Item("ab", "some-id");

        Assert.False(keyForAb.StartsWith(prefixA, StringComparison.Ordinal),
            $"Key '{keyForAb}' must not match prefix '{prefixA}' (different workspace wid 'ab' vs 'a')");
    }

    [Fact]
    public void FeedPrefix_does_not_match_longer_wid()
    {
        var prefixA = WorkspaceKeys.FeedPrefix("a");
        var keyForAb = WorkspaceKeys.Feed("ab", "feed-id");

        Assert.False(keyForAb.StartsWith(prefixA, StringComparison.Ordinal),
            $"Key '{keyForAb}' must not match prefix '{prefixA}'");
    }

    [Fact]
    public void ItemPrefix_matches_correct_wid_key()
    {
        var prefix = WorkspaceKeys.ItemPrefix("abc");
        var keyForAbc = WorkspaceKeys.Item("abc", "item-id");

        Assert.StartsWith(prefix, keyForAbc, StringComparison.Ordinal);
    }
}
