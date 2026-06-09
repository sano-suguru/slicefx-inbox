namespace Inbox.Client.Components;

/// <summary>
/// Payload passed from <see cref="InboxItemCard"/> to the parent page when an item's status
/// changes via a row action (Mark Read / Archive / Unread).
/// Defined as a top-level record (not nested inside InboxItemCard) so razor pages can reference
/// it without full qualification — <c>_Imports.razor</c> already has <c>@using Inbox.Client.Components</c>.
/// </summary>
public sealed record StatusChange(string Id, string Status);
