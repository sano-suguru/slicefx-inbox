using Inbox.Contracts;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Infrastructure;

/// <summary>
/// Prefix-scan helpers that replace the mutable index lists
/// (<c>*:index</c> keys) previously used for listing items, feeds, and workspaces.
/// Each entry is stored under its own key; listing is done by calling
/// <see cref="IKeyValueStore.ListKeysAsync"/> and filtering by key prefix.
/// This eliminates the read-modify-write races inherent in maintaining
/// whole-array index keys shared across concurrent writers.
/// </summary>
/// <remarks>
/// <para>
/// <b>Consistency note:</b> these methods depend on <c>get-keys</c>
/// (the <c>fermyon:spin/key-value@2.0.0</c> WIT <c>get-keys</c> operation) reflecting
/// a key written by a prior <c>set</c> call — i.e., enumeration-after-write consistency.
/// This is a stronger requirement than the single-key read-after-write the index approach
/// relied on.  The <c>fermyon:spin/key-value@2.0.0</c> WIT specification does not
/// document consistency semantics; in practice local Spin (SQLite-backed) and Fermyon
/// Cloud's managed KV store both appear to be strongly consistent, but this is an
/// upstream assumption rather than a contract guarantee.
/// </para>
/// <para>
/// <b>Performance:</b> <see cref="IKeyValueStore.ListKeysAsync"/> returns every key in
/// the store (there is no server-side prefix filter).  Per-request list operations
/// (<see cref="ListItemsAsync"/>, <see cref="ListFeedsAsync"/>) each issue one full-store
/// key scan followed by per-item fetches.  This is acceptable at dogfood scale (tens of
/// workspaces, hundreds of items) but would degrade as O(total keys) at larger scale.
/// The cron batch path (<see cref="PartitionAsync"/>) issues a single key scan and
/// partitions the result in memory to avoid O(W × total-keys) blowup.
/// </para>
/// </remarks>
internal static class KvScan
{
    /// <summary>
    /// Returns all items for <paramref name="wid"/>, sorted by
    /// <see cref="InboxItem.SavedAt"/> ascending then by <see cref="InboxItem.Id"/>
    /// (stable tiebreaker for items with identical timestamps, e.g. entries in a single
    /// feed refresh that have no <c>pubDate</c>).
    /// </summary>
    public static async ValueTask<InboxItem[]> ListItemsAsync(
        IKeyValueStore kv, string wid, CancellationToken ct)
    {
        var prefix = WorkspaceKeys.ItemPrefix(wid);
        var keys = await kv.ListKeysAsync(ct);
        var items = new List<InboxItem>();

        foreach (var key in keys)
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var item = await kv.GetJsonAsync(key, InboxJsonContext.Default.InboxItem, ct);
            if (item is not null) items.Add(item);
        }

        items.Sort(static (a, b) =>
        {
            var cmp = a.SavedAt.CompareTo(b.SavedAt);
            return cmp != 0 ? cmp : string.Compare(a.Id, b.Id, StringComparison.Ordinal);
        });

        return [.. items];
    }

    /// <summary>
    /// Returns all feed subscriptions for <paramref name="wid"/>, sorted by
    /// <see cref="FeedSubscription.AddedAt"/> ascending then by <see cref="FeedSubscription.Id"/>.
    /// </summary>
    public static async ValueTask<FeedSubscription[]> ListFeedsAsync(
        IKeyValueStore kv, string wid, CancellationToken ct)
    {
        var prefix = WorkspaceKeys.FeedPrefix(wid);
        var keys = await kv.ListKeysAsync(ct);
        var feeds = new List<FeedSubscription>();

        foreach (var key in keys)
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var feed = await kv.GetJsonAsync(key, InboxJsonContext.Default.FeedSubscription, ct);
            if (feed is not null) feeds.Add(feed);
        }

        feeds.Sort(static (a, b) =>
        {
            var cmp = a.AddedAt.CompareTo(b.AddedAt);
            return cmp != 0 ? cmp : string.Compare(a.Id, b.Id, StringComparison.Ordinal);
        });

        return [.. feeds];
    }

    /// <summary>
    /// Returns all workspace IDs by scanning <c>workspace:{wid}</c> keys and reading
    /// the wid from each stored <see cref="Workspace"/> body.
    /// This avoids substring extraction, keeping NativeAOT-LLVM WASI compatibility safe.
    /// </summary>
    public static async ValueTask<string[]> ListWorkspaceIdsAsync(
        IKeyValueStore kv, CancellationToken ct)
    {
        const string prefix = WorkspaceKeys.WorkspacePrefix;
        var keys = await kv.ListKeysAsync(ct);
        var wids = new List<string>();

        foreach (var key in keys)
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var workspace = await kv.GetJsonAsync(key, InboxJsonContext.Default.Workspace, ct);
            if (workspace is not null) wids.Add(workspace.Id);
        }

        return [.. wids];
    }

    /// <summary>
    /// Returns the total number of workspaces currently stored.
    /// Used as the <see cref="WorkspaceProvisioner.MaxWorkspaces"/> guard.
    /// Note: the count check is still subject to a TOCTOU race between concurrent
    /// <c>CreateWorkspace</c> requests — this matches the previous index-length approach.
    /// </summary>
    public static async ValueTask<int> CountWorkspacesAsync(
        IKeyValueStore kv, CancellationToken ct)
    {
        const string prefix = WorkspaceKeys.WorkspacePrefix;
        var keys = await kv.ListKeysAsync(ct);
        var count = 0;
        foreach (var key in keys)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal)) count++;
        }
        return count;
    }

    /// <summary>
    /// Scans the entire KV store once and returns a dictionary that maps each workspace ID
    /// to its items and feeds.  Used by the cron batch path to avoid issuing one
    /// <see cref="IKeyValueStore.ListKeysAsync"/> call per workspace.
    /// </summary>
    public static async ValueTask<Dictionary<string, WorkspacePartition>> PartitionAsync(
        IKeyValueStore kv, CancellationToken ct)
    {
        var allKeys = await kv.ListKeysAsync(ct);
        var partitions = new Dictionary<string, WorkspacePartition>(StringComparer.Ordinal);

        foreach (var key in allKeys)
        {
            // w:{wid}:item:{id}  or  w:{wid}:feed:{id}
            if (!key.StartsWith("w:", StringComparison.Ordinal)) continue;

            // Find the wid segment: between the first ':' and the second ':'
            var firstColon = key.IndexOf(':');         // after "w"
            var secondColon = firstColon >= 0 ? key.IndexOf(':', firstColon + 1) : -1;
            if (firstColon < 0 || secondColon < 0) continue;

            var afterWid = key[(secondColon + 1)..];
            // afterWid is "item:{id}" or "feed:{id}" — skip the dead "items:index" / "feeds:index" legacy keys
            if (afterWid.StartsWith("items:index", StringComparison.Ordinal) ||
                afterWid.StartsWith("feeds:index", StringComparison.Ordinal))
                continue;
            if (!afterWid.StartsWith("item:", StringComparison.Ordinal) &&
                !afterWid.StartsWith("feed:", StringComparison.Ordinal))
                continue;

            var wid = key[(firstColon + 1)..secondColon];
            if (!partitions.TryGetValue(wid, out var partition))
            {
                partition = new WorkspacePartition(wid);
                partitions[wid] = partition;
            }

            if (afterWid.StartsWith("item:", StringComparison.Ordinal))
                partition.ItemKeys.Add(key);
            else
                partition.FeedKeys.Add(key);
        }

        return partitions;
    }
}

/// <summary>
/// Per-workspace key sets produced by <see cref="KvScan.PartitionAsync"/>.
/// </summary>
internal sealed class WorkspacePartition(string wid)
{
    public string Wid { get; } = wid;
    public List<string> ItemKeys { get; } = [];
    public List<string> FeedKeys { get; } = [];
}
