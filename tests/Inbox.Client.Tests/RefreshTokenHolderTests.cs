using Inbox.Client;

namespace Inbox.Client.Tests;

public class RefreshTokenHolderTests
{
    // ──────────────────────────────────────────────
    // Fake ISessionStorage backed by a Dictionary
    // ──────────────────────────────────────────────
    private sealed class FakeSessionStorage : ISessionStorage
    {
        private readonly Dictionary<string, string> _store = [];

        public ValueTask<string?> GetItemAsync(string key)
            => ValueTask.FromResult(_store.TryGetValue(key, out var v) ? v : null);

        public ValueTask SetItemAsync(string key, string value)
        {
            _store[key] = value;
            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveItemAsync(string key)
        {
            _store.Remove(key);
            return ValueTask.CompletedTask;
        }

        public bool Contains(string key) => _store.ContainsKey(key);
        public string? Get(string key) => _store.TryGetValue(key, out var v) ? v : null;
    }

    [Fact]
    public async Task InitializeAsync_hydrates_token_from_storage()
    {
        var storage = new FakeSessionStorage();
        await storage.SetItemAsync("inbox_refresh_token", "stored-token");

        var holder = new RefreshTokenHolder(storage);
        await holder.InitializeAsync();

        Assert.Equal("stored-token", holder.Token);
    }

    [Fact]
    public async Task InitializeAsync_fires_Changed_event()
    {
        var storage = new FakeSessionStorage();
        var holder = new RefreshTokenHolder(storage);

        var fired = false;
        holder.Changed += () => fired = true;
        await holder.InitializeAsync();

        Assert.True(fired);
    }

    [Fact]
    public async Task InitializeAsync_leaves_token_null_when_storage_empty()
    {
        var holder = new RefreshTokenHolder(new FakeSessionStorage());
        await holder.InitializeAsync();

        Assert.Null(holder.Token);
    }

    [Theory]
    [InlineData("  hello  ", "hello")]
    [InlineData("hello", "hello")]
    [InlineData("\t token \n", "token")]
    public async Task SetAsync_trims_whitespace_from_token(string input, string expected)
    {
        var holder = new RefreshTokenHolder(new FakeSessionStorage());
        await holder.SetAsync(input);

        Assert.Equal(expected, holder.Token);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    [InlineData(null)]
    public async Task SetAsync_converts_blank_to_null(string? input)
    {
        var holder = new RefreshTokenHolder(new FakeSessionStorage());
        await holder.SetAsync(input);

        Assert.Null(holder.Token);
    }

    [Fact]
    public async Task SetAsync_writes_non_null_token_to_storage()
    {
        var storage = new FakeSessionStorage();
        var holder = new RefreshTokenHolder(storage);

        await holder.SetAsync("my-token");

        Assert.Equal("my-token", storage.Get("inbox_refresh_token"));
    }

    [Fact]
    public async Task SetAsync_removes_from_storage_when_null()
    {
        var storage = new FakeSessionStorage();
        await storage.SetItemAsync("inbox_refresh_token", "old-token");

        var holder = new RefreshTokenHolder(storage);
        await holder.SetAsync(null);

        Assert.False(storage.Contains("inbox_refresh_token"));
    }

    [Fact]
    public async Task SetAsync_removes_from_storage_when_blank()
    {
        var storage = new FakeSessionStorage();
        await storage.SetItemAsync("inbox_refresh_token", "old-token");

        var holder = new RefreshTokenHolder(storage);
        await holder.SetAsync("   ");

        Assert.False(storage.Contains("inbox_refresh_token"));
    }

    [Fact]
    public async Task SetAsync_fires_Changed_event()
    {
        var holder = new RefreshTokenHolder(new FakeSessionStorage());

        var fired = false;
        holder.Changed += () => fired = true;
        await holder.SetAsync("token");

        Assert.True(fired);
    }

    [Fact]
    public async Task SetAsync_fires_Changed_even_when_clearing()
    {
        var holder = new RefreshTokenHolder(new FakeSessionStorage());

        var fired = false;
        holder.Changed += () => fired = true;
        await holder.SetAsync(null);

        Assert.True(fired);
    }
}
