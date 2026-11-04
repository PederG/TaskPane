namespace TaskPane.Core.Enums;

public enum MonthlyRecurrenceMode
{
    /// <summary>Day-of-month (e.g. 31st → last day when month is shorter).</summary>
    DayOfMonth = 0,

    /// <summary>Nth weekday of month (e.g. first Monday); WeekOfMonth=-1 means last.</summary>
    NthWeekday = 1
}
