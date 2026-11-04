using TaskPane.Core.Enums;
using TaskPane.Core.Errors;
using TaskPane.Core.Recurrence;

namespace TaskPane.Core.Tests;

public sealed class RecurrenceCalculatorTests
{
    [Fact]
    public void Month_end_clamps_day_31_in_february_including_leap_year()
    {
        Assert.Equal(new DateOnly(2026, 2, 28), RecurrenceCalculator.ClampDayOfMonth(2026, 2, 31));
        Assert.Equal(new DateOnly(2024, 2, 29), RecurrenceCalculator.ClampDayOfMonth(2024, 2, 31));
        Assert.Equal(new DateOnly(2026, 4, 30), RecurrenceCalculator.ClampDayOfMonth(2026, 4, 31));
    }

    [Fact]
    public void Monthly_day_31_enumerates_clamped_month_ends()
    {
        var rule = new RecurrenceRule
        {
            Frequency = RecurrenceFrequency.Monthly,
            MonthlyMode = MonthlyRecurrenceMode.DayOfMonth,
            DayOfMonth = 31,
            Interval = 1,
            AnchorLocalDate = "2026-01-31"
        };

        var dates = RecurrenceCalculator.EnumerateFrom(rule, new DateOnly(2026, 1, 1))
            .Take(4)
            .ToList();

        Assert.Equal(
            [new DateOnly(2026, 1, 31), new DateOnly(2026, 2, 28), new DateOnly(2026, 3, 31), new DateOnly(2026, 4, 30)],
            dates);
    }

    [Fact]
    public void First_monday_of_month_computes_correctly()
    {
        // November 2026: first Monday is the 2nd.
        Assert.Equal(
            new DateOnly(2026, 11, 2),
            RecurrenceCalculator.ComputeNthWeekday(2026, 11, 1, DayOfWeek.Monday));
    }

    [Fact]
    public void Selected_weekdays_skip_weekend()
    {
        var rule = new RecurrenceRule
        {
            Frequency = RecurrenceFrequency.SelectedWeekdays,
            Weekdays = [1, 2, 3, 4, 5], // Mon–Fri
            AnchorLocalDate = "2026-11-06" // Friday
        };

        var next = RecurrenceCalculator.NextAfter(rule, new DateOnly(2026, 11, 6));
        Assert.Equal(new DateOnly(2026, 11, 9), next); // Monday
    }

    [Fact]
    public void Occurrence_key_round_trips()
    {
        var key = OccurrenceKey.FromLocal(new DateOnly(2026, 11, 4), new TimeOnly(9, 30));
        Assert.True(OccurrenceKey.TryParse(key, out var date, out var time));
        Assert.Equal(new DateOnly(2026, 11, 4), date);
        Assert.Equal(new TimeOnly(9, 30), time);

        var dateOnly = OccurrenceKey.FromLocal(new DateOnly(2026, 11, 4), null);
        Assert.Equal("2026-11-04", dateOnly);
    }

    [Fact]
    public void Invalid_rule_throws()
    {
        var ex = Assert.Throws<BusinessException>(() =>
            RecurrenceCalculator.Validate(new RecurrenceRule
            {
                Frequency = RecurrenceFrequency.SelectedWeekdays,
                Weekdays = [],
                AnchorLocalDate = "2026-11-04"
            }));
        Assert.Equal(ValidationCodes.InvalidRecurrenceRule, ex.Code);
    }

    [Fact]
    public void Dst_spring_forward_rolls_invalid_local_time()
    {
        // US Pacific spring forward 2026-03-08: 02:00 does not exist.
        var tz = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "Pacific Standard Time" : "America/Los_Angeles");
        var utc = LocalScheduleTime.ToUtc(new DateOnly(2026, 3, 8), new TimeOnly(2, 30), tz);
        var local = TimeZoneInfo.ConvertTime(utc, tz);
        Assert.False(tz.IsInvalidTime(DateTime.SpecifyKind(local.DateTime, DateTimeKind.Unspecified)));
        Assert.True(local.Hour >= 3 || local.Date > new DateTime(2026, 3, 8));
    }

    [Fact]
    public void Dst_fall_back_uses_standard_offset()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "Pacific Standard Time" : "America/Los_Angeles");
        // 2026-11-01 01:30 is ambiguous.
        var utc = LocalScheduleTime.ToUtc(new DateOnly(2026, 11, 1), new TimeOnly(1, 30), tz);
        Assert.False(tz.IsDaylightSavingTime(utc.UtcDateTime));
    }
}
