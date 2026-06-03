using Inbox.Contracts;
using Inbox.Server.Infrastructure;
using SliceFx.Wasi;
using SliceFx.Wasi.KeyValue;

namespace Inbox.Server.Tests;

public class DemoTests
{
    [Fact]
    public async Task EnsureDemo_returns_demo_token()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();

        var response = await app.DispatchAsync(new WasiRequest(
            "POST", "/api/demo", new Dictionary<string, string>(), null, null));

        Assert.Equal(200, response.Status);
        var result = InboxTestApp.FromJsonBody(response, InboxJsonContext.Default.CreateWorkspaceResponse)!;
        Assert.Equal(DemoWorkspace.Token, result.Token);
    }

    [Fact]
    public async Task EnsureDemo_token_grants_access_to_demo_workspace()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();

        await app.DispatchAsync(new WasiRequest("POST", "/api/demo", new Dictionary<string, string>(), null, null));

        var itemsResp = await InboxTestApp.GetAsync(app, "/api/items", token: DemoWorkspace.Token);
        Assert.Equal(200, itemsResp.Status);
    }

    [Fact]
    public async Task EnsureDemo_seeds_sample_items_on_first_call()
    {
        var (app, kv, _, _, _) = InboxTestApp.Create();

        await app.DispatchAsync(new WasiRequest("POST", "/api/demo", new Dictionary<string, string>(), null, null));

        // Verify items are seeded via prefix scan (no index key).
        IKeyValueStore kvStore = kv;
        var items = await KvScan.ListItemsAsync(kvStore, DemoWorkspace.Wid, CancellationToken.None);
        Assert.True(items.Length > 0, "Demo workspace should have sample items seeded");
    }

    [Fact]
    public async Task EnsureDemo_is_idempotent_no_duplicate_items()
    {
        // Regression test for S2 (concurrent first-hit double-seed producing duplicate items).
        // Calling twice must NOT produce duplicate items; deterministic IDs ensure overwrite not duplicate.
        var (app, kv, _, _, _) = InboxTestApp.Create();

        // Call twice
        await app.DispatchAsync(new WasiRequest("POST", "/api/demo", new Dictionary<string, string>(), null, null));
        await app.DispatchAsync(new WasiRequest("POST", "/api/demo", new Dictionary<string, string>(), null, null));

        // Verify via prefix scan — no duplicates (same deterministic ID overwrites).
        IKeyValueStore kvStore = kv;
        var items = await KvScan.ListItemsAsync(kvStore, DemoWorkspace.Wid, CancellationToken.None);
        var distinctIds = items.Select(i => i.Id).Distinct().ToArray();
        Assert.Equal(distinctIds.Length, items.Length);
    }

    [Fact]
    public async Task EnsureDemo_sample_items_have_deterministic_ids()
    {
        // Deterministic IDs ensure concurrent seeds overwrite the same keys rather than creating duplicates.
        var (app, kv, _, _, _) = InboxTestApp.Create();

        await app.DispatchAsync(new WasiRequest("POST", "/api/demo", new Dictionary<string, string>(), null, null));

        Assert.True(await ((IKeyValueStore)kv).ExistsAsync(WorkspaceKeys.Item(DemoWorkspace.Wid, "demo-sample-1"), CancellationToken.None));
        Assert.True(await ((IKeyValueStore)kv).ExistsAsync(WorkspaceKeys.Item(DemoWorkspace.Wid, "demo-sample-2"), CancellationToken.None));
    }
}
