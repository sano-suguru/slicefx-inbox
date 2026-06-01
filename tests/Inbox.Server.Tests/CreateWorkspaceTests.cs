using Inbox.Contracts;
using Inbox.Server.Infrastructure;
using SliceFx.Wasi;
using SliceFx.Wasi.KeyValue;

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
    public async Task CreateWorkspace_allows_registration_when_registration_open_not_set()
    {
        // Fail-open: InboxTestApp.Create() does not set registration_open, so GetAsync returns null → allowed.
        // This is the default behavior — no need to explicitly set anything.
        var (app, _, _, _, _) = InboxTestApp.Create();

        var response = await app.DispatchAsync(new WasiRequest(
            "POST", "/api/workspaces", new Dictionary<string, string>(), null, null));

        Assert.Equal(200, response.Status);
    }

    [Fact]
    public async Task CreateWorkspace_returns_429_when_workspace_limit_reached()
    {
        var (app, kv, _, _, _) = InboxTestApp.Create();

        // Fill the workspaces:index beyond MaxWorkspaces
        IKeyValueStore kvStore = kv;
        var fakeIndex = Enumerable.Range(0, WorkspaceProvisioner.MaxWorkspaces)
            .Select(i => $"fake-wid-{i}")
            .ToArray();
        await kvStore.SetJsonAsync(WorkspaceKeys.WorkspacesIndex, fakeIndex,
            InboxJsonContext.Default.StringArray, CancellationToken.None);

        var response = await app.DispatchAsync(new WasiRequest(
            "POST", "/api/workspaces", new Dictionary<string, string>(), null, null));

        Assert.Equal(429, response.Status);
    }

    [Fact]
    public async Task CreateWorkspace_adds_wid_to_workspaces_index()
    {
        var (app, kv, _, _, _) = InboxTestApp.Create();

        var createResp = await app.DispatchAsync(new WasiRequest(
            "POST", "/api/workspaces", new Dictionary<string, string>(), null, null));
        Assert.Equal(200, createResp.Status);

        IKeyValueStore kvStore = kv;
        var index = await kvStore.GetJsonAsync(WorkspaceKeys.WorkspacesIndex,
            InboxJsonContext.Default.StringArray, CancellationToken.None);
        // Should contain both the default seeded wid and the newly created one
        Assert.NotNull(index);
        Assert.True(index.Length >= 2);
    }
}
