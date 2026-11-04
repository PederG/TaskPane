using TaskPane.Core.Enums;
using TaskPane.Core.Errors;

namespace TaskPane.Core.Recurrence;

/// <summary>Deterministic occurrence date generation for recurrence rules.</summary>
public static class RecurrenceCalculator
{
    public static void Validate(RecurrenceRule rule)
    {
        if (rule.Version <= 0)
        {
            throw new BusinessException(ValidationCodes.InvalidRecurrenceRule, "Recurrence rule version is required.");
        }

        if (rule.Interval < 1)
        {
            throw new BusinessException(ValidationCodes.InvalidRecurrenceRule, "Recurrence interval must be >= 1.");
        }

        if (string.IsNullOrWhiteSpace(rule.AnchorLocalDate)
            || !DateOnly.TryParse(rule.AnchorLocalDate, out _))
        {
            throw new BusinessException(ValidationCodes.InvalidRecurrenceRule, "Anchor local date is required (YYYY-MM-DD).");
        }

        if (!string.IsNullOrWhiteSpace(rule.LocalTime) && !TimeOnly.TryParse(rule.LocalTime, out _))
        {
            throw new BusinessException(ValidationCodes.InvalidRecurrenceRule, "Local time must be HH:mm.");
        }

        switch (rule.Frequency)
        {
            case RecurrenceFrequency.SelectedWeekdays:
                if (rule.Weekdays is null || rule.Weekdays.Length == 0)
                {
                    throw new BusinessException(
                        ValidationCodes.InvalidRecurrenceRule,
                        "Selected weekdays require at least one weekday.");
                }

                if (rule.Weekdays.Any(d => d is < 0 or > 6))
                {
                    throw new BusinessException(ValidationCodes.InvalidRecurrenceRule, "Weekdays must be 0–6.");
                }

                break;

            case RecurrenceFrequency.Monthly when rule.MonthlyMode == MonthlyRecurrenceMode.DayOfMonth:
                if (rule.DayOfMonth is null or < 1 or > 31)
                {
                    throw new BusinessException(
                        ValidationCodes.InvalidRecurrenceRule,
                        "Monthly day-of-month must be 1–31.");
                }

                break;

            case RecurrenceFrequency.Monthly when rule.MonthlyMode == MonthlyRecurrenceMode.NthWeekday:
                if (rule.WeekdayOfMonth is null)
                {
                    throw new BusinessException(
                        ValidationCodes.InvalidRecurrenceRule,
                        "Monthly nth-weekday requires a weekday.");
                }

                if (rule.WeekOfMonth is null or (< 1 and not -1) or > 4)
                {
                    throw new BusinessException(
                        ValidationCodes.InvalidRecurrenceRule,
                        "Week of month must be 1–4 or -1 (last).");
                }

                break;
        }
    }

    public static TimeOnly? GetLocalTime(RecurrenceRule rule)
        => string.IsNullOrWhiteSpace(rule.LocalTime)
            ? null
            : TimeOnly.Parse(rule.LocalTime);

    public static DateOnly GetAnchor(RecurrenceRule rule) => DateOnly.Parse(rule.AnchorLocalDate);

    /// <summary>
    /// Returns the occurrence local date on or before <paramref name="onOrBefore"/> that matches the rule,
    /// or null if the anchor is after that date.
    /// </summary>
    public static DateOnly? LatestOnOrBefore(RecurrenceRule rule, DateOnly onOrBefore)
    {
        Validate(rule);
        var anchor = GetAnchor(rule);
        if (onOrBefore < anchor)
        {
            return null;
        }

        // Walk forward from anchor with a bounded loop; for long gaps jump by interval.
        DateOnly? latest = null;
        foreach (var date in EnumerateFrom(rule, anchor))
        {
            if (date > onOrBefore)
            {
                break;
            }

            latest = date;
            // Safety: avoid pathological infinite loops.
            if (date > onOrBefore.AddYears(50))
            {
                break;
            }
        }

        return latest;
    }

    /// <summary>Next occurrence strictly after <paramref name="after"/>.</summary>
    public static DateOnly? NextAfter(RecurrenceRule rule, DateOnly after)
    {
        Validate(rule);
        var anchor = GetAnchor(rule);
        var start = after < anchor ? anchor.AddDays(-1) : after;
        foreach (var date in EnumerateFrom(rule, anchor))
        {
            if (date > start && date > after)
            {
                return date;
            }

            if (date > after.AddYears(50))
            {
                break;
            }
        }

        return null;
    }

    public static IEnumerable<DateOnly> EnumerateFrom(RecurrenceRule rule, DateOnly fromInclusive)
    {
        Validate(rule);
        return rule.Frequency switch
        {
            RecurrenceFrequency.Daily => EnumerateDaily(rule, fromInclusive),
            RecurrenceFrequency.CustomIntervalDays => EnumerateDaily(rule, fromInclusive),
            RecurrenceFrequency.Weekly => EnumerateWeekly(rule, fromInclusive),
            RecurrenceFrequency.SelectedWeekdays => EnumerateSelectedWeekdays(rule, fromInclusive),
            RecurrenceFrequency.Monthly => EnumerateMonthly(rule, fromInclusive),
            _ => throw new BusinessException(ValidationCodes.InvalidRecurrenceRule, "Unknown frequency.")
        };
    }

    private static IEnumerable<DateOnly> EnumerateDaily(RecurrenceRule rule, DateOnly fromInclusive)
    {
        var anchor = GetAnchor(rule);
        var interval = Math.Max(1, rule.Interval);
        var cursor = anchor;
        if (fromInclusive > anchor)
        {
            var days = fromInclusive.DayNumber - anchor.DayNumber;
            var steps = days / interval;
            cursor = anchor.AddDays(steps * interval);
            if (cursor < fromInclusive)
            {
                cursor = cursor.AddDays(interval);
            }
        }

        while (true)
        {
            if (cursor >= fromInclusive)
            {
                yield return cursor;
            }

            cursor = cursor.AddDays(interval);
        }
        // ReSharper disable once IteratorNeverReturns
    }

    private static IEnumerable<DateOnly> EnumerateWeekly(RecurrenceRule rule, DateOnly fromInclusive)
    {
        var anchor = GetAnchor(rule);
        var intervalWeeks = Math.Max(1, rule.Interval);
        var targetDow = rule.Weekdays is { Length: > 0 }
            ? (DayOfWeek)rule.Weekdays[0]
            : anchor.DayOfWeek;

        // Align to the weekday on/after anchor within the first week.
        var first = AlignToWeekdayOnOrAfter(anchor, targetDow);
        var cursor = first;
        while (cursor < fromInclusive)
        {
            cursor = cursor.AddDays(7 * intervalWeeks);
        }

        while (true)
        {
            yield return cursor;
            cursor = cursor.AddDays(7 * intervalWeeks);
        }
    }

    private static IEnumerable<DateOnly> EnumerateSelectedWeekdays(RecurrenceRule rule, DateOnly fromInclusive)
    {
        var anchor = GetAnchor(rule);
        var days = rule.Weekdays!.Distinct().OrderBy(d => d).ToArray();
        var cursor = fromInclusive < anchor ? anchor : fromInclusive;

        while (true)
        {
            if (cursor >= anchor && days.Contains((int)cursor.DayOfWeek))
            {
                yield return cursor;
            }

            cursor = cursor.AddDays(1);
        }
    }

    private static IEnumerable<DateOnly> EnumerateMonthly(RecurrenceRule rule, DateOnly fromInclusive)
    {
        var anchor = GetAnchor(rule);
        var interval = Math.Max(1, rule.Interval);
        var year = anchor.Year;
        var month = anchor.Month;

        while (true)
        {
            var date = rule.MonthlyMode == MonthlyRecurrenceMode.NthWeekday
                ? ComputeNthWeekday(year, month, rule.WeekOfMonth!.Value, rule.WeekdayOfMonth!.Value)
                : ClampDayOfMonth(year, month, rule.DayOfMonth ?? anchor.Day);

            if (date >= anchor && date >= fromInclusive)
            {
                yield return date;
            }

            month += interval;
            while (month > 12)
            {
                month -= 12;
                year++;
            }
        }
    }

    /// <summary>Month-end policy: day 31 in shorter months uses the last day of that month.</summary>
    public static DateOnly ClampDayOfMonth(int year, int month, int dayOfMonth)
    {
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var day = Math.Clamp(dayOfMonth, 1, daysInMonth);
        return new DateOnly(year, month, day);
    }

    public static DateOnly ComputeNthWeekday(int year, int month, int weekOfMonth, DayOfWeek weekday)
    {
        if (weekOfMonth == -1)
        {
            var lastDay = DateTime.DaysInMonth(year, month);
            var cursor = new DateOnly(year, month, lastDay);
            while (cursor.DayOfWeek != weekday)
            {
                cursor = cursor.AddDays(-1);
            }

            return cursor;
        }

        var first = new DateOnly(year, month, 1);
        var aligned = AlignToWeekdayOnOrAfter(first, weekday);
        return aligned.AddDays(7 * (weekOfMonth - 1));
    }

    private static DateOnly AlignToWeekdayOnOrAfter(DateOnly start, DayOfWeek weekday)
    {
        var delta = ((int)weekday - (int)start.DayOfWeek + 7) % 7;
        return start.AddDays(delta);
    }
}
