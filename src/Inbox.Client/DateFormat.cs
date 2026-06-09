namespace Inbox.Client;

/// <summary>
/// Centralised date/time formatting helpers so all pages use the same display patterns.
/// </summary>
internal static class DateFormat
{
    /// <summary>Short date: <c>yyyy-MM-dd</c> — used in item card metadata.</summary>
    public static string Short(DateTimeOffset dt) => dt.ToString("yyyy-MM-dd");

    /// <summary>Long date: <c>yyyy-MM-dd HH:mm</c> — used in item detail view.</summary>
    public static string Long(DateTimeOffset dt) => dt.ToString("yyyy-MM-dd HH:mm");
}
