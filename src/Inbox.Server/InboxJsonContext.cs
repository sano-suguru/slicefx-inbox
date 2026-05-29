using System.Text.Json.Serialization;
using Inbox.Contracts;
using Inbox.Server.Features.Items;
using Inbox.Server.Features.Spikes;

namespace Inbox.Server;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(PostItem.Request), TypeInfoPropertyName = "PostItemRequest")]
[JsonSerializable(typeof(PostItem.Response), TypeInfoPropertyName = "PostItemResponse")]
[JsonSerializable(typeof(GetItems.Response), TypeInfoPropertyName = "GetItemsResponse")]
[JsonSerializable(typeof(GetItem.Response), TypeInfoPropertyName = "GetItemResponse")]
[JsonSerializable(typeof(GetOutboundTest.Response), TypeInfoPropertyName = "GetOutboundTestResponse")]
[JsonSerializable(typeof(InboxItem), TypeInfoPropertyName = "InboxItem")]
[JsonSerializable(typeof(InboxItem[]), TypeInfoPropertyName = "InboxItemArray")]
[JsonSerializable(typeof(string[]), TypeInfoPropertyName = "StringArray")]
[SliceJsonContext(SliceJsonTarget.Wasi)]
public sealed partial class InboxJsonContext : JsonSerializerContext
{
}
