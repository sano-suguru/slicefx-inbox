using Inbox.Client;

namespace Inbox.Client.Tests;

public class DateFormatTests
{
    [Theory]
    [InlineData(2025, 1, 5, "2025-01-05")]
    [InlineData(2025, 12, 31, "2025-12-31")]
    [InlineData(2000, 1, 1, "2000-01-01")]
    public void Short_returns_yyyy_MM_dd(int year, int month, int day, string expected)
    {
        var dt = new DateTimeOffset(year, month, day, 14, 30, 0, TimeSpan.Zero);
        Assert.Equal(expected, DateFormat.Short(dt));
    }

    [Theory]
    [InlineData(2025, 6, 12, 9, 5, "2025-06-12 09:05")]
    [InlineData(2025, 1, 1, 0, 0, "2025-01-01 00:00")]
    [InlineData(2025, 12, 31, 23, 59, "2025-12-31 23:59")]
    public void Long_returns_yyyy_MM_dd_HH_mm(int year, int month, int day, int hour, int minute, string expected)
    {
        var dt = new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero);
        Assert.Equal(expected, DateFormat.Long(dt));
    }

    [Fact]
    public void Short_does_not_include_time()
    {
        var dt = new DateTimeOffset(2025, 3, 15, 23, 59, 59, TimeSpan.Zero);
        Assert.DoesNotContain(":", DateFormat.Short(dt));
    }
}
