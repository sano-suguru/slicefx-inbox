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

    /// <summary>
    /// Fixed token for the shared demo workspace.
    /// This token is intentionally public — it grants read-write access to the shared demo data.
    /// </summary>
    internal const string DemoToken = "demo-access-token";

    internal const string DemoWid = "demo";

    /// <summary>
    /// Creates a new workspace. Returns (wid, token) or null if the workspace limit is reached.
    /// </summary>
    public static async Task<(string Wid, string Token)?> CreateAsync(IKeyValueStore kv, CancellationToken ct)
    {
        var index = await kv.GetJsonAsync(WorkspaceKeys.WorkspacesIndex, InboxJsonContext.Default.StringArray, ct) ?? [];
        if (index.Length >= MaxWorkspaces) return null;

        var wid = Guid.NewGuid().ToString("N");
        // Two Guid values concatenated for ~244-bit token length.
        // Note: this improves collision resistance but not prediction resistance if Guid.NewGuid()
        // is not CSPRNG-backed in this WASI runtime (quality unconfirmed).
        var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

        var workspace = new Workspace(wid, DateTimeOffset.UtcNow, IsDemo: false);
        await kv.SetJsonAsync(WorkspaceKeys.Workspace(wid), workspace, InboxJsonContext.Default.Workspace, ct);
        await kv.SetStringAsync(WorkspaceKeys.Token(token), wid, ct);

        // Re-read index before appending to reduce (but not eliminate) the concurrent-registration
        // lost-update window. Contains guard avoids double-appending the same wid.
        var latestIndex = await kv.GetJsonAsync(WorkspaceKeys.WorkspacesIndex, InboxJsonContext.Default.StringArray, ct) ?? [];
        if (!Array.Exists(latestIndex, x => x == wid))
            await kv.SetJsonAsync(WorkspaceKeys.WorkspacesIndex, [.. latestIndex, wid], InboxJsonContext.Default.StringArray, ct);

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

            var index = await kv.GetJsonAsync(WorkspaceKeys.WorkspacesIndex, InboxJsonContext.Default.StringArray, ct) ?? [];
            if (!Array.Exists(index, x => x == DemoWid))
                await kv.SetJsonAsync(WorkspaceKeys.WorkspacesIndex, [.. index, DemoWid], InboxJsonContext.Default.StringArray, ct);

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
        await kv.SetJsonAsync(WorkspaceKeys.Item(DemoWid, id), item, InboxJsonContext.Default.InboxItem, ct);

        var index = await kv.GetJsonAsync(WorkspaceKeys.ItemsIndex(DemoWid), InboxJsonContext.Default.StringArray, ct) ?? [];
        if (!Array.Exists(index, x => x == id))
            await kv.SetJsonAsync(WorkspaceKeys.ItemsIndex(DemoWid), [.. index, id], InboxJsonContext.Default.StringArray, ct);
    }

    private static async Task SeedDemoFeedAsync(
        IKeyValueStore kv, string id, string feedUrl, string title, CancellationToken ct)
    {
        if (await kv.ExistsAsync(WorkspaceKeys.Feed(DemoWid, id), ct)) return;

        var feed = new FeedSubscription(id, feedUrl, title, DateTimeOffset.UtcNow);
        await kv.SetJsonAsync(WorkspaceKeys.Feed(DemoWid, id), feed, InboxJsonContext.Default.FeedSubscription, ct);

        var index = await kv.GetJsonAsync(WorkspaceKeys.FeedsIndex(DemoWid), InboxJsonContext.Default.StringArray, ct) ?? [];
        if (!Array.Exists(index, x => x == id))
            await kv.SetJsonAsync(WorkspaceKeys.FeedsIndex(DemoWid), [.. index, id], InboxJsonContext.Default.StringArray, ct);
    }
}
