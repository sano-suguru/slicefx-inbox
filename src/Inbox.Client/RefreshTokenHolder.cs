namespace Inbox.Client;

/// <summary>
/// Singleton in-memory cache for the operator refresh token.
/// The token is entered at runtime via the UI (never baked into the build artifact)
/// and also persisted to <c>sessionStorage</c> so it survives page refreshes within
/// the same tab — but is cleared when the tab is closed.
/// </summary>
public sealed class RefreshTokenHolder(ISessionStorage storage)
{
    private const string StorageKey = "inbox_refresh_token";

    private readonly ISessionStorage _storage = storage;

    public string? Token { get; private set; }

    /// <summary>Raised when the token is set or cleared.</summary>
    public event Action? Changed;

    /// <summary>
    /// Hydrate from sessionStorage. Call once from the root layout's
    /// <c>OnInitializedAsync</c> (after JS interop is available).
    /// </summary>
    public async Task InitializeAsync()
    {
        Token = await _storage.GetItemAsync(StorageKey);
        Changed?.Invoke();
    }

    /// <summary>Set or clear the token. Also writes through to sessionStorage.</summary>
    public async Task SetAsync(string? token)
    {
        Token = string.IsNullOrWhiteSpace(token) ? null : token.Trim();
        if (Token is not null)
            await _storage.SetItemAsync(StorageKey, Token);
        else
            await _storage.RemoveItemAsync(StorageKey);
        Changed?.Invoke();
    }
}
