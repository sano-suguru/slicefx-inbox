using Inbox.Contracts;
using Inbox.Server.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using SliceFx;
using SliceFx.Wasi;
using SliceFx.Wasi.HttpClient;
using SliceFx.Wasi.KeyValue;
using SliceFx.Wasi.Spin;

namespace Inbox.Server.Tests;

public class CreateWorkspaceTests
{
    [Fact]
    public async Task CreateWorkspace_returns_a_token()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();

        var response = await app.DispatchAsync(new WasiRequest(
            "POST", "/api/workspaces", new Dictionary<string, string>(), null, null));

        Assert.Equal(200, response.Status);
        var result = InboxTestApp.FromJsonBody(response, InboxJsonContext.Default.CreateWorkspaceResponse)!;
        Assert.False(string.IsNullOrWhiteSpace(result.Token));
    }

    [Fact]
    public async Task CreateWorkspace_token_can_authenticate()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();

        var createResp = await app.DispatchAsync(new WasiRequest(
            "POST", "/api/workspaces", new Dictionary<string, string>(), null, null));
        var created = InboxTestApp.FromJsonBody(createResp, InboxJsonContext.Default.CreateWorkspaceResponse)!;

        // Use the new token to access the workspace
        var itemsResp = await InboxTestApp.GetAsync(app, "/api/items", token: created.Token);
        Assert.Equal(200, itemsResp.Status);
    }

    [Fact]
    public async Task CreateWorkspace_tokens_are_unique()
    {
        var (app, _, _, _, _) = InboxTestApp.Create();

        var t1 = InboxTestApp.FromJsonBody(
            await app.DispatchAsync(new WasiRequest("POST", "/api/workspaces", new Dictionary<string, string>(), null, null)),
            InboxJsonContext.Default.CreateWorkspaceResponse)!.Token;
        var t2 = InboxTestApp.FromJsonBody(
            await app.DispatchAsync(new WasiRequest("POST", "/api/workspaces", new Dictionary<string, string>(), null, null)),
            InboxJsonContext.Default.CreateWorkspaceResponse)!.Token;

        Assert.NotEqual(t1, t2);
    }

    [Fact]
    public async Task CreateWorkspace_returns_403_when_registration_open_is_false()
    {
        var (app, _, _, vars, _) = InboxTestApp.Create();
        vars.Set("registration_open", "false");

        var response = await app.DispatchAsync(new WasiRequest(
            "POST", "/api/workspaces", new Dictionary<string, string>(), null, null));

        Assert.Equal(403, response.Status);
    }

    [Fact]
    public async Task CreateWorkspace_returns_403_when_registration_open_is_not_set()
    {
        // Fail-closed: null / WIT-error → 403 (same as "false").
        // InboxTestApp.Create() sets registration_open = "true"; we override with a fresh builder
        // that has no registration_open set so GetAsync returns null.
        var builder = WasiHost.CreateBuilder();
        builder.AddSlice();
        builder.Services.AddSingleton(TimeProvider.System);
        var kv = new InMemoryKeyValueStore();
        var vars = new InMemorySpinVariables();
        vars.Set("cron_token", InboxTestApp.DefaultCronToken);
        // Deliberately do NOT set registration_open — simulates WIT read error or unconfigured variable.
        builder.Services.AddSingleton<IKeyValueStore>(kv);
        builder.Services.AddSingleton<SliceFx.Wasi.HttpClient.IWasiHttpClient>(new InMemoryWasiHttpClient());
        builder.AddSpinVariables(vars);
        builder.Services.AddSingleton<IAuthenticator>(new KvAuthenticator(kv));
        var app = builder.Build();

        var response = await app.DispatchAsync(new WasiRequest(
            "POST", "/api/workspaces", new Dictionary<string, string>(), null, null));

        Assert.Equal(403, response.Status);
    }

    [Fact]
    public async Task CreateWorkspace_allows_registration_when_registration_open_is_true()
    {
        // Explicit "true" → allowed.
        var (app, _, _, vars, _) = InboxTestApp.Create();
        vars.Set("registration_open", "true"); // already set by Create(); explicit for clarity

        var response = await app.DispatchAsync(new WasiRequest(
            "POST", "/api/workspaces", new Dictionary<string, string>(), null, null));

        Assert.Equal(200, response.Status);
    }

    [Fact]
    public async Task CreateWorkspace_returns_429_when_workspace_limit_reached()
    {
        var (app, kv, _, _, _) = InboxTestApp.Create();

        // Seed MaxWorkspaces workspace:{wid} keys directly — no index needed.
        // KvScan.CountWorkspacesAsync counts workspace: prefix keys.
        IKeyValueStore kvStore = kv;
        for (var i = 0; i < WorkspaceProvisioner.MaxWorkspaces; i++)
        {
            var fakeWid = $"fake-wid-{i}";
            var fakeWorkspace = new Workspace(fakeWid, DateTimeOffset.UtcNow);
            await kvStore.SetJsonAsync(WorkspaceKeys.Workspace(fakeWid), fakeWorkspace,
                InboxJsonContext.Default.Workspace, CancellationToken.None);
        }

        var response = await app.DispatchAsync(new WasiRequest(
            "POST", "/api/workspaces", new Dictionary<string, string>(), null, null));

        Assert.Equal(429, response.Status);
    }

    [Fact]
    public async Task CreateWorkspace_workspace_is_discoverable_via_prefix_scan()
    {
        var (app, kv, _, _, _) = InboxTestApp.Create();

        var createResp = await app.DispatchAsync(new WasiRequest(
            "POST", "/api/workspaces", new Dictionary<string, string>(), null, null));
        Assert.Equal(200, createResp.Status);
        var created = InboxTestApp.FromJsonBody(createResp, InboxJsonContext.Default.CreateWorkspaceResponse)!;

        // Workspace must be discoverable via KvScan prefix scan (used by cron orchestration).
        IKeyValueStore kvStore = kv;
        var wids = await KvScan.ListWorkspaceIdsAsync(kvStore, CancellationToken.None);
        // Should contain the default seeded wid and the newly created one.
        Assert.True(wids.Length >= 2);
        // The new workspace's token must resolve to a valid wid.
        var resolvedWid = await kvStore.GetStringAsync(WorkspaceKeys.Token(created.Token), CancellationToken.None);
        Assert.NotNull(resolvedWid);
        Assert.Contains(wids, w => w == resolvedWid);
    }
}
