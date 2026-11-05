using System.Globalization;
using System.Text;
using TaskPane.Core.Enums;
using TaskPane.Core.Errors;

namespace TaskPane.Core.Recurrence;

/// <summary>Human-readable recurrence rule summaries and UI-facing validation.</summary>
public static class RecurrenceRuleFormatter
{
    private static readonly string[] WeekdayNames =
        ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    public static bool TryValidate(RecurrenceRule rule, out string? error)
    {
        try
        {
            RecurrenceCalculator.Validate(rule);
            error = null;
            return true;
        }
        catch (BusinessException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static string Summarize(RecurrenceRule rule)
    {
        if (!TryValidate(rule, out _))
        {
            return "Invalid recurrence rule";
        }

        var interval = Math.Max(1, rule.Interval);
        var sb = new StringBuilder();

        switch (rule.Frequency)
        {
            case RecurrenceFrequency.Daily when interval == 1:
                sb.Append("Every day");
                break;
            case RecurrenceFrequency.Daily:
            case RecurrenceFrequency.CustomIntervalDays:
                sb.Append(CultureInfo.InvariantCulture, $"Every {interval} days");
                break;
            case RecurrenceFrequency.Weekly when interval == 1:
                sb.Append("Weekly");
                AppendWeekdays(sb, rule.Weekdays);
                break;
            case RecurrenceFrequency.Weekly:
                sb.Append(CultureInfo.InvariantCulture, $"Every {interval} weeks");
                AppendWeekdays(sb, rule.Weekdays);
                break;
            case RecurrenceFrequency.SelectedWeekdays:
                sb.Append("On ");
                AppendWeekdayList(sb, rule.Weekdays);
                break;
            case RecurrenceFrequency.Monthly:
                AppendMonthly(sb, rule, interval);
                break;
            default:
                sb.Append(rule.Frequency.ToString());
                break;
        }

        if (!string.IsNullOrWhiteSpace(rule.LocalTime))
        {
            sb.Append(CultureInfo.InvariantCulture, $" at {rule.LocalTime}");
        }

        if (!string.IsNullOrWhiteSpace(rule.AnchorLocalDate))
        {
            sb.Append(CultureInfo.InvariantCulture, $" · from {rule.AnchorLocalDate}");
        }

        return sb.ToString();
    }

    private static void AppendMonthly(StringBuilder sb, RecurrenceRule rule, int interval)
    {
        if (interval == 1)
        {
            sb.Append("Monthly");
        }
        else
        {
            sb.Append(CultureInfo.InvariantCulture, $"Every {interval} months");
        }

        if (rule.MonthlyMode == MonthlyRecurrenceMode.NthWeekday
            && rule.WeekOfMonth is { } week
            && rule.WeekdayOfMonth is { } weekday)
        {
            sb.Append(CultureInfo.InvariantCulture, $" on the {FormatWeekOfMonth(week)} {weekday}");
        }
        else if (rule.DayOfMonth is { } day)
        {
            sb.Append(CultureInfo.InvariantCulture, $" on day {day}");
        }
    }

    private static void AppendWeekdays(StringBuilder sb, int[]? weekdays)
    {
        if (weekdays is not { Length: > 0 })
        {
            return;
        }

        sb.Append(" on ");
        AppendWeekdayList(sb, weekdays);
    }

    private static void AppendWeekdayList(StringBuilder sb, int[]? weekdays)
    {
        if (weekdays is not { Length: > 0 })
        {
            sb.Append("(no weekdays)");
            return;
        }

        var names = weekdays
            .Where(d => d is >= 0 and <= 6)
            .Distinct()
            .OrderBy(d => d)
            .Select(d => WeekdayNames[d]);
        sb.Append(string.Join(", ", names));
    }

    private static string FormatWeekOfMonth(int weekOfMonth) => weekOfMonth switch
    {
        1 => "first",
        2 => "second",
        3 => "third",
        4 => "fourth",
        -1 => "last",
        _ => weekOfMonth.ToString(CultureInfo.InvariantCulture)
    };
}
