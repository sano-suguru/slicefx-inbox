using System.Text.Json.Serialization;
using Inbox.Contracts;

namespace Inbox.Server;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
// Items
[JsonSerializable(typeof(PostItemRequest), TypeInfoPropertyName = "PostItemRequest")]
[JsonSerializable(typeof(PostItemResponse), TypeInfoPropertyName = "PostItemResponse")]
[JsonSerializable(typeof(GetItemsResponse), TypeInfoPropertyName = "GetItemsResponse")]
[JsonSerializable(typeof(GetItemResponse), TypeInfoPropertyName = "GetItemResponse")]
[JsonSerializable(typeof(UpdateItemRequest), TypeInfoPropertyName = "UpdateItemRequest")]
// Feeds
[JsonSerializable(typeof(AddFeedRequest), TypeInfoPropertyName = "AddFeedRequest")]
[JsonSerializable(typeof(AddFeedResponse), TypeInfoPropertyName = "AddFeedResponse")]
[JsonSerializable(typeof(GetFeedsResponse), TypeInfoPropertyName = "GetFeedsResponse")]
[JsonSerializable(typeof(RefreshFeedsResponse), TypeInfoPropertyName = "RefreshFeedsResponse")]
// Domain / KV records
[JsonSerializable(typeof(InboxItem), TypeInfoPropertyName = "InboxItem")]
[JsonSerializable(typeof(InboxItem[]), TypeInfoPropertyName = "InboxItemArray")]
[JsonSerializable(typeof(FeedSubscription), TypeInfoPropertyName = "FeedSubscription")]
[JsonSerializable(typeof(FeedSubscription[]), TypeInfoPropertyName = "FeedSubscriptionArray")]
[JsonSerializable(typeof(string[]), TypeInfoPropertyName = "StringArray")]
// Workspace records
[JsonSerializable(typeof(Workspace), TypeInfoPropertyName = "Workspace")]
[JsonSerializable(typeof(CreateWorkspaceResponse), TypeInfoPropertyName = "CreateWorkspaceResponse")]
[SliceJsonContext(SliceJsonTarget.Wasi)]
public sealed partial class InboxJsonContext : JsonSerializerContext
{
}
