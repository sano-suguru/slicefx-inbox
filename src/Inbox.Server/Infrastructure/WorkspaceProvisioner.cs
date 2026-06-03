using Inbox.Contracts;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Infrastructure;

/// <summary>
/// Creates and seeds workspaces in KV.
/// </summary>
internal static class WorkspaceProvisioner
{
    /// <summary>Maximum number of workspaces allowed (abuse guard for Fermyon free tier).</summary>
    internal const int MaxWorkspaces = 1000;

    // DemoToken / DemoWid are defined in Inbox.Contracts.DemoWorkspace so both
    // the server and the Blazor WASM client share a single source of truth.
    private const string DemoToken = DemoWorkspace.Token;
    private const string DemoWid = DemoWorkspace.Wid;

    /// <summary>
    /// Creates a new workspace. Returns (wid, token) or null if the workspace limit is reached.
    /// </summary>
    public static async Task<(string Wid, string Token)?> CreateAsync(IKeyValueStore kv, CancellationToken ct)
    {
        // Count via prefix scan — still subject to the same TOCTOU race as the previous
        // index-length approach; two concurrent creates can both pass the check.
        var count = await KvScan.CountWorkspacesAsync(kv, ct);
        if (count >= MaxWorkspaces) return null;

        var wid = Guid.NewGuid().ToString("N");
        // Two Guid values concatenated for ~244-bit token length.
        // Note: this improves collision resistance but not prediction resistance if Guid.NewGuid()
        // is not CSPRNG-backed in this WASI runtime (quality unconfirmed).
        var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

        var workspace = new Workspace(wid, DateTimeOffset.UtcNow, IsDemo: false);
        await kv.SetJsonAsync(WorkspaceKeys.Workspace(wid), workspace, InboxJsonContext.Default.Workspace, ct);
        await kv.SetStringAsync(WorkspaceKeys.Token(token), wid, ct);

        // No index update — workspace listing is derived from KvScan.ListWorkspaceIdsAsync,
        // which scans workspace:* keys directly.
        return (wid, token);
    }

    /// <summary>
    /// Seeds the demo workspace idempotently. Returns the demo token.
    /// Workspace creation uses deterministic IDs — safe under concurrent first-hits.
    /// Feed seeding runs even when the workspace already exists, so feeds can be added post-deploy.
    /// </summary>
    public static async Task<string> EnsureDemoAsync(IKeyValueStore kv, CancellationToken ct)
    {
        if (!await kv.ExistsAsync(WorkspaceKeys.Workspace(DemoWid), ct))
        {
            var workspace = new Workspace(DemoWid, DateTimeOffset.UtcNow, IsDemo: true);
            await kv.SetJsonAsync(WorkspaceKeys.Workspace(DemoWid), workspace, InboxJsonContext.Default.Workspace, ct);
            await kv.SetStringAsync(WorkspaceKeys.Token(DemoToken), DemoWid, ct);

            // No index update — listing is derived from KvScan prefix scans.

            // Seed sample bookmarks (deterministic IDs — concurrent seeds overwrite rather than duplicate).
            await SeedDemoItemAsync(kv, "demo-sample-1",
                "https://spinframework.dev/", "Spin — The Developer Tool for WASI", "bookmark", ct);
            await SeedDemoItemAsync(kv, "demo-sample-2",
                "https://github.com/sano-suguru/slicefx", "SliceFx — Vertical Slice Architecture for .NET WASI", "bookmark", ct);
        }

        // Feed seeding runs regardless — safe to call on existing workspaces (idempotent per feed ID).
        await SeedDemoFeedAsync(kv, "demo-feed-1", "https://zenn.dev/topics/csharp/feed", "Zenn C#", ct);
        await SeedDemoFeedAsync(kv, "demo-feed-2", "https://zenn.dev/topics/dotnet/feed", "Zenn .NET", ct);
        await SeedDemoFeedAsync(kv, "demo-feed-3", "https://zenn.dev/topics/wasm/feed", "Zenn WASM", ct);
        await SeedDemoFeedAsync(kv, "demo-feed-4", "https://github.com/sano-suguru/slicefx/releases.atom", "SliceFx Releases", ct);
        await SeedDemoFeedAsync(kv, "demo-feed-5", "https://github.com/spinframework/spin/releases.atom", "Spin Releases", ct);
        await SeedDemoFeedAsync(kv, "demo-feed-6", "https://github.com/bytecodealliance/wasmtime/releases.atom", "Wasmtime Releases", ct);
        await SeedDemoFeedAsync(kv, "demo-feed-7", "https://devblogs.microsoft.com/dotnet/feed/", ".NET Blog", ct);
        await SeedDemoFeedAsync(kv, "demo-feed-8", "https://bytecodealliance.org/feed.xml", "Bytecode Alliance Blog", ct);

        return DemoToken;
    }

    private static async Task SeedDemoItemAsync(
        IKeyValueStore kv, string id, string url, string title, string source, CancellationToken ct)
    {
        var item = new InboxItem(id, url, title, null, ItemStatus.Unread, DateTimeOffset.UtcNow, source);
        // Single-key write — no index update needed.
        await kv.SetJsonAsync(WorkspaceKeys.Item(DemoWid, id), item, InboxJsonContext.Default.InboxItem, ct);
    }

    private static async Task SeedDemoFeedAsync(
        IKeyValueStore kv, string id, string feedUrl, string title, CancellationToken ct)
    {
        if (await kv.ExistsAsync(WorkspaceKeys.Feed(DemoWid, id), ct)) return;

        var feed = new FeedSubscription(id, feedUrl, title, DateTimeOffset.UtcNow);
        // Single-key write — no index update needed.
        await kv.SetJsonAsync(WorkspaceKeys.Feed(DemoWid, id), feed, InboxJsonContext.Default.FeedSubscription, ct);
    }
}
