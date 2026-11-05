using TaskPane.Core.Entities;
using TaskPane.Core.Reminders;

namespace TaskPane.Core.Tests;

public sealed class QuietHoursEvaluatorTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    [Theory]
    [InlineData("2026-11-04T21:59:00Z", false)]
    [InlineData("2026-11-04T22:00:00Z", true)]
    [InlineData("2026-11-04T23:30:00Z", true)]
    [InlineData("2026-11-05T06:59:00Z", true)]
    [InlineData("2026-11-05T07:00:00Z", false)]
    [InlineData("2026-11-05T12:00:00Z", false)]
    public void Default_quiet_hours_span_midnight(string utc, bool expected)
    {
        var settings = new ReminderSettings
        {
            QuietHoursEnabled = true,
            QuietStartLocal = "22:00",
            QuietEndLocal = "07:00"
        };

        Assert.Equal(expected, QuietHoursEvaluator.IsInQuietHours(settings, DateTimeOffset.Parse(utc), Utc));
    }

    [Fact]
    public void Disabled_quiet_hours_never_active()
    {
        var settings = new ReminderSettings
        {
            QuietHoursEnabled = false,
            QuietStartLocal = "22:00",
            QuietEndLocal = "07:00"
        };

        Assert.False(QuietHoursEvaluator.IsInQuietHours(
            settings,
            DateTimeOffset.Parse("2026-11-04T23:00:00Z"),
            Utc));
    }

    [Theory]
    [InlineData("2026-11-05T11:59:00Z", false)]
    [InlineData("2026-11-05T12:00:00Z", true)]
    [InlineData("2026-11-05T13:30:00Z", true)]
    [InlineData("2026-11-05T14:00:00Z", false)]
    public void Same_day_quiet_window(string utc, bool expected)
    {
        var settings = new ReminderSettings
        {
            QuietHoursEnabled = true,
            QuietStartLocal = "12:00",
            QuietEndLocal = "14:00"
        };

        Assert.Equal(expected, QuietHoursEvaluator.IsInQuietHours(settings, DateTimeOffset.Parse(utc), Utc));
    }
}
