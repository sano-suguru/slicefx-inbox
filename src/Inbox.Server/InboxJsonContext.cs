using System.Text.Json.Serialization;
using Inbox.Contracts;
using Inbox.Server.Features.Feeds;
using Inbox.Server.Features.Items;

namespace Inbox.Server;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
// Items
[JsonSerializable(typeof(PostItem.Request), TypeInfoPropertyName = "PostItemRequest")]
[JsonSerializable(typeof(PostItem.Response), TypeInfoPropertyName = "PostItemResponse")]
[JsonSerializable(typeof(GetItems.Response), TypeInfoPropertyName = "GetItemsResponse")]
[JsonSerializable(typeof(GetItem.Response), TypeInfoPropertyName = "GetItemResponse")]
// Feeds
[JsonSerializable(typeof(AddFeed.Request), TypeInfoPropertyName = "AddFeedRequest")]
[JsonSerializable(typeof(AddFeed.Response), TypeInfoPropertyName = "AddFeedResponse")]
[JsonSerializable(typeof(GetFeeds.Response), TypeInfoPropertyName = "GetFeedsResponse")]
[JsonSerializable(typeof(RefreshFeeds.Response), TypeInfoPropertyName = "RefreshFeedsResponse")]
// Domain / KV records
[JsonSerializable(typeof(InboxItem), TypeInfoPropertyName = "InboxItem")]
[JsonSerializable(typeof(InboxItem[]), TypeInfoPropertyName = "InboxItemArray")]
[JsonSerializable(typeof(FeedSubscription), TypeInfoPropertyName = "FeedSubscription")]
[JsonSerializable(typeof(FeedSubscription[]), TypeInfoPropertyName = "FeedSubscriptionArray")]
[JsonSerializable(typeof(string[]), TypeInfoPropertyName = "StringArray")]
[SliceJsonContext(SliceJsonTarget.Wasi)]
public sealed partial class InboxJsonContext : JsonSerializerContext
{
}
