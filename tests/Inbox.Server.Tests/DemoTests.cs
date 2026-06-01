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
        Assert.Equal(WorkspaceProvisioner.DemoToken, result.Token);
    }

    [Fact]
    public async Task EnsureDemo_token_grants_access_to_demo_workspace()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();

        await app.DispatchAsync(new WasiRequest("POST", "/api/demo", new Dictionary<string, string>(), null, null));

        var itemsResp = await InboxTestApp.GetAsync(app, "/api/items", token: WorkspaceProvisioner.DemoToken);
        Assert.Equal(200, itemsResp.Status);
    }

    [Fact]
    public async Task EnsureDemo_seeds_sample_items_on_first_call()
    {
        var (app, kv, _, _, _) = InboxTestApp.Create();

        await app.DispatchAsync(new WasiRequest("POST", "/api/demo", new Dictionary<string, string>(), null, null));

        IKeyValueStore kvStore = kv;
        var index = await kvStore.GetJsonAsync(
            WorkspaceKeys.ItemsIndex(WorkspaceProvisioner.DemoWid),
            InboxJsonContext.Default.StringArray, CancellationToken.None);
        Assert.NotNull(index);
        Assert.True(index.Length > 0, "Demo workspace should have sample items seeded");
    }

    [Fact]
    public async Task EnsureDemo_is_idempotent_no_duplicate_items()
    {
        // Regression test for S2 (concurrent first-hit double-seed producing duplicate items).
        // Calling twice must NOT produce duplicate items in the index.
        var (app, kv, _, _, _) = InboxTestApp.Create();

        // Call twice
        await app.DispatchAsync(new WasiRequest("POST", "/api/demo", new Dictionary<string, string>(), null, null));
        await app.DispatchAsync(new WasiRequest("POST", "/api/demo", new Dictionary<string, string>(), null, null));

        IKeyValueStore kvStore = kv;
        var index = await kvStore.GetJsonAsync(
            WorkspaceKeys.ItemsIndex(WorkspaceProvisioner.DemoWid),
            InboxJsonContext.Default.StringArray, CancellationToken.None);
        Assert.NotNull(index);

        // Verify no duplicates in the index
        var distinct = index.Distinct().ToArray();
        Assert.Equal(distinct.Length, index.Length);
    }

    [Fact]
    public async Task EnsureDemo_sample_items_have_deterministic_ids()
    {
        // Deterministic IDs ensure concurrent seeds overwrite the same keys rather than creating duplicates.
        var (app, kv, _, _, _) = InboxTestApp.Create();

        await app.DispatchAsync(new WasiRequest("POST", "/api/demo", new Dictionary<string, string>(), null, null));

        IKeyValueStore kvStore = kv;
        Assert.True(await kvStore.ExistsAsync(WorkspaceKeys.Item(WorkspaceProvisioner.DemoWid, "demo-sample-1"), CancellationToken.None));
        Assert.True(await kvStore.ExistsAsync(WorkspaceKeys.Item(WorkspaceProvisioner.DemoWid, "demo-sample-2"), CancellationToken.None));
    }
}
