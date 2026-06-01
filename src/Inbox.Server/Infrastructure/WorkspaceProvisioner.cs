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
    /// Concurrent first-hits are safe: item IDs are deterministic (same keys get overwritten, not duplicated).
    /// </summary>
    public static async Task<string> EnsureDemoAsync(IKeyValueStore kv, CancellationToken ct)
    {
        // If workspace already exists, skip seeding entirely — early return avoids duplicate items.
        if (await kv.ExistsAsync(WorkspaceKeys.Workspace(DemoWid), ct))
            return DemoToken;

        var workspace = new Workspace(DemoWid, DateTimeOffset.UtcNow, IsDemo: true);
        await kv.SetJsonAsync(WorkspaceKeys.Workspace(DemoWid), workspace, InboxJsonContext.Default.Workspace, ct);
        await kv.SetStringAsync(WorkspaceKeys.Token(DemoToken), DemoWid, ct);

        // Add demo wid to the workspace index (Contains guard against double-append)
        var index = await kv.GetJsonAsync(WorkspaceKeys.WorkspacesIndex, InboxJsonContext.Default.StringArray, ct) ?? [];
        if (!Array.Exists(index, x => x == DemoWid))
            await kv.SetJsonAsync(WorkspaceKeys.WorkspacesIndex, [.. index, DemoWid], InboxJsonContext.Default.StringArray, ct);

        // Seed sample items with deterministic IDs so that concurrent seeds overwrite rather than duplicate.
        await SeedDemoItemAsync(kv, "demo-sample-1",
            "https://docs.fermyon.com/spin/v3/", "Spin Documentation — Getting Started", "bookmark", ct);
        await SeedDemoItemAsync(kv, "demo-sample-2",
            "https://github.com/fermyon/spin", "Spin — build WASI components", "bookmark", ct);

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
}
