using TaskPane.Core.Enums;

namespace TaskPane.Core.Recurrence;

/// <summary>Versioned recurring schedule model persisted as RuleJson.</summary>
public sealed class RecurrenceRule
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public RecurrenceFrequency Frequency { get; set; } = RecurrenceFrequency.Daily;

    /// <summary>Every N units (days/weeks/months depending on frequency). Minimum 1.</summary>
    public int Interval { get; set; } = 1;

    /// <summary>ISO day-of-week ints 0=Sunday..6=Saturday for SelectedWeekdays / Weekly.</summary>
    public int[]? Weekdays { get; set; }

    public MonthlyRecurrenceMode MonthlyMode { get; set; } = MonthlyRecurrenceMode.DayOfMonth;

    /// <summary>1–31; missing days clamp to month end.</summary>
    public int? DayOfMonth { get; set; }

    /// <summary>1–4 or -1 for last week of month (NthWeekday mode).</summary>
    public int? WeekOfMonth { get; set; }

    public DayOfWeek? WeekdayOfMonth { get; set; }

    /// <summary>Optional local time HH:mm. Null means date-only occurrence.</summary>
    public string? LocalTime { get; set; }

    /// <summary>First occurrence local calendar date (YYYY-MM-DD).</summary>
    public string AnchorLocalDate { get; set; } = string.Empty;
}
