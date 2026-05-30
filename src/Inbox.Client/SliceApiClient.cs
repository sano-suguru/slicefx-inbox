using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Inbox.Contracts;

namespace Inbox.Client;

// ─────────────────────────────────────────────────────────────────────────────
// Hand-written typed client over the SliceFx Inbox API (same-origin).
//
// NOTE: The slicefx CLI (`dotnet tool run slicefx -- client csharp`) was run
// as dogfood evidence (A.5-2 step 5) after the DTO migration.  The output is
// committed separately as SliceApiClient.evidence.g.cs.  It emits a usable
// GetItemsAsync / GetFeedsAsync, but every WasiResponse-returning route
// produces a broken Task<SliceFx.Wasi.WasiResponse> method that does not
// deserialize the body.  This hand-written client is the shipped version;
// it becomes regenerable once the framework's client generator handles
// WasiResponse-returning features.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Typed client for the SliceFx Inbox API.</summary>
public sealed partial class SliceApiClient(HttpClient http)
{
    public ItemsClient Items { get; } = new(http);
    public FeedsClient Feeds { get; } = new(http);

    // ─────────────────────────── Items ───────────────────────────────────────

    public sealed class ItemsClient(HttpClient http)
    {
        /// <summary>List inbox items. Null/empty filter args are omitted from the query string.</summary>
        public async Task<GetItemsResponse> GetItemsAsync(
            string? q = null, string? tag = null, string? status = null,
            CancellationToken ct = default)
        {
            var url = BuildUrl("/api/items", ("q", q), ("tag", tag), ("status", status));
            using var msg = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await http.SendAsync(msg, ct).ConfigureAwait(false);
            await EnsureSuccessAsync(resp, "GetItems", ct).ConfigureAwait(false);
            return await resp.Content.ReadFromJsonAsync(
                InboxClientJsonContext.Default.GetItemsResponse, ct).ConfigureAwait(false)
                ?? throw new SliceApiException("GetItems returned an empty response body.", HttpStatusCode.OK);
        }

        /// <summary>Get a single inbox item by id.</summary>
        public async Task<GetItemResponse> GetItemAsync(string id, CancellationToken ct = default)
        {
            using var msg = new HttpRequestMessage(HttpMethod.Get, $"/api/items/{Uri.EscapeDataString(id)}");
            using var resp = await http.SendAsync(msg, ct).ConfigureAwait(false);
            await EnsureSuccessAsync(resp, "GetItem", ct).ConfigureAwait(false);
            return await resp.Content.ReadFromJsonAsync(
                InboxClientJsonContext.Default.GetItemResponse, ct).ConfigureAwait(false)
                ?? throw new SliceApiException("GetItem returned an empty response body.", HttpStatusCode.OK);
        }

        /// <summary>Save a URL for later reading. Requires token.</summary>
        public async Task<PostItemResponse> PostItemAsync(PostItemRequest req, CancellationToken ct = default)
        {
            using var msg = new HttpRequestMessage(HttpMethod.Post, "/api/items");
            msg.Content = JsonContent.Create(req, InboxClientJsonContext.Default.PostItemRequest);
            using var resp = await http.SendAsync(msg, ct).ConfigureAwait(false);
            await EnsureSuccessAsync(resp, "PostItem", ct).ConfigureAwait(false);
            return await resp.Content.ReadFromJsonAsync(
                InboxClientJsonContext.Default.PostItemResponse, ct).ConfigureAwait(false)
                ?? throw new SliceApiException("PostItem returned an empty response body.", HttpStatusCode.OK);
        }

        /// <summary>Update status and/or tags on an inbox item. Requires token.</summary>
        public async Task UpdateItemAsync(string id, UpdateItemRequest req, CancellationToken ct = default)
        {
            using var msg = new HttpRequestMessage(HttpMethod.Patch, $"/api/items/{Uri.EscapeDataString(id)}");
            msg.Content = JsonContent.Create(req, InboxClientJsonContext.Default.UpdateItemRequest);
            using var resp = await http.SendAsync(msg, ct).ConfigureAwait(false);
            await EnsureSuccessAsync(resp, "UpdateItem", ct).ConfigureAwait(false);
        }

        /// <summary>Remove an inbox item. Requires token.</summary>
        public async Task DeleteItemAsync(string id, CancellationToken ct = default)
        {
            using var msg = new HttpRequestMessage(HttpMethod.Delete, $"/api/items/{Uri.EscapeDataString(id)}");
            using var resp = await http.SendAsync(msg, ct).ConfigureAwait(false);
            await EnsureSuccessAsync(resp, "DeleteItem", ct).ConfigureAwait(false);
        }
    }

    // ─────────────────────────── Feeds ───────────────────────────────────────

    public sealed class FeedsClient(HttpClient http)
    {
        /// <summary>List feed subscriptions.</summary>
        public async Task<GetFeedsResponse> GetFeedsAsync(CancellationToken ct = default)
        {
            using var msg = new HttpRequestMessage(HttpMethod.Get, "/api/feeds");
            using var resp = await http.SendAsync(msg, ct).ConfigureAwait(false);
            await EnsureSuccessAsync(resp, "GetFeeds", ct).ConfigureAwait(false);
            return await resp.Content.ReadFromJsonAsync(
                InboxClientJsonContext.Default.GetFeedsResponse, ct).ConfigureAwait(false)
                ?? throw new SliceApiException("GetFeeds returned an empty response body.", HttpStatusCode.OK);
        }

        /// <summary>Subscribe to an RSS or Atom feed. Requires token.</summary>
        public async Task<AddFeedResponse> AddFeedAsync(AddFeedRequest req, CancellationToken ct = default)
        {
            using var msg = new HttpRequestMessage(HttpMethod.Post, "/api/feeds");
            msg.Content = JsonContent.Create(req, InboxClientJsonContext.Default.AddFeedRequest);
            using var resp = await http.SendAsync(msg, ct).ConfigureAwait(false);
            await EnsureSuccessAsync(resp, "AddFeed", ct).ConfigureAwait(false);
            return await resp.Content.ReadFromJsonAsync(
                InboxClientJsonContext.Default.AddFeedResponse, ct).ConfigureAwait(false)
                ?? throw new SliceApiException("AddFeed returned an empty response body.", HttpStatusCode.OK);
        }

        /// <summary>Fetch all subscribed feeds and ingest new items. Requires token.</summary>
        public async Task<RefreshFeedsResponse> RefreshFeedsAsync(CancellationToken ct = default)
        {
            using var msg = new HttpRequestMessage(HttpMethod.Post, "/api/feeds/refresh");
            using var resp = await http.SendAsync(msg, ct).ConfigureAwait(false);
            await EnsureSuccessAsync(resp, "RefreshFeeds", ct).ConfigureAwait(false);
            return await resp.Content.ReadFromJsonAsync(
                InboxClientJsonContext.Default.RefreshFeedsResponse, ct).ConfigureAwait(false)
                ?? throw new SliceApiException("RefreshFeeds returned an empty response body.", HttpStatusCode.OK);
        }
    }

    // ─────────────────────────── Helpers ─────────────────────────────────────

    private static string BuildUrl(string path, params (string key, string? value)[] queryParams)
    {
        var qs = string.Join("&", queryParams
            .Where(p => !string.IsNullOrEmpty(p.value))
            .Select(p => $"{Uri.EscapeDataString(p.key)}={Uri.EscapeDataString(p.value!)}"));
        return string.IsNullOrEmpty(qs) ? path : $"{path}?{qs}";
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage resp, string route, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;

        SliceProblemDetails? problem = null;
        var ct2 = resp.Content.Headers.ContentType?.MediaType;
        if (ct2 is "application/problem+json" or "application/json")
        {
            try
            {
                problem = await resp.Content
                    .ReadFromJsonAsync(InboxClientJsonContext.Default.SliceProblemDetails, ct)
                    .ConfigureAwait(false);
            }
            catch (JsonException) { }
        }

        throw new SliceApiException(
            problem?.Detail ?? problem?.Title ?? $"Route '{route}' returned HTTP {(int)resp.StatusCode}.",
            resp.StatusCode,
            problem);
    }
}

// ─────────────────────────── Exception ───────────────────────────────────────

/// <summary>Thrown when the API returns a non-success status.</summary>
public sealed class SliceApiException(
    string message,
    HttpStatusCode statusCode,
    SliceProblemDetails? problem = null)
    : HttpRequestException(message)
{
    public new HttpStatusCode StatusCode { get; } = statusCode;
    public SliceProblemDetails? Problem { get; } = problem;
    public IReadOnlyDictionary<string, string[]>? Errors => Problem?.Errors;
}

/// <summary>
/// Problem details payload from the server (RFC 7807 / ProblemDetails).
/// </summary>
public sealed class SliceProblemDetails
{
    public string? Type { get; set; }
    public string? Title { get; set; }
    public int? Status { get; set; }
    public string? Detail { get; set; }
    public Dictionary<string, string[]>? Errors { get; set; }
}

// ─────────────────────────── JSON context ────────────────────────────────────

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(PostItemRequest))]
[JsonSerializable(typeof(PostItemResponse))]
[JsonSerializable(typeof(GetItemsResponse))]
[JsonSerializable(typeof(GetItemResponse))]
[JsonSerializable(typeof(UpdateItemRequest))]
[JsonSerializable(typeof(AddFeedRequest))]
[JsonSerializable(typeof(AddFeedResponse))]
[JsonSerializable(typeof(GetFeedsResponse))]
[JsonSerializable(typeof(RefreshFeedsResponse))]
[JsonSerializable(typeof(InboxItem))]
[JsonSerializable(typeof(FeedSubscription))]
[JsonSerializable(typeof(SliceProblemDetails))]
internal sealed partial class InboxClientJsonContext : JsonSerializerContext
{
}
