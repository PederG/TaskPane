using TaskPane.Core.Entities;

namespace TaskPane.Core.Deadline;

/// <summary>
/// Date-only deadlines stay date-only (never coerced to midnight UTC).
/// Timed deadlines evaluate in the task's timezone (or local when unset).
/// </summary>
public static class DeadlineEvaluator
{
    public static bool IsOverdue(TaskItem task, DateTimeOffset utcNow, TimeZoneInfo? fallbackLocal = null)
    {
        if (task.IsCompleted || task.IsDeleted || string.IsNullOrWhiteSpace(task.DeadlineLocalDate))
        {
            return false;
        }

        if (!DateOnly.TryParse(task.DeadlineLocalDate, out var date))
        {
            return false;
        }

        var tz = ResolveTimeZone(task.DeadlineTimeZoneId, fallbackLocal);
        var localNow = TimeZoneInfo.ConvertTime(utcNow, tz);

        if (string.IsNullOrWhiteSpace(task.DeadlineLocalTime))
        {
            // Date-only: overdue once the local calendar day is past the deadline date.
            return DateOnly.FromDateTime(localNow.DateTime) > date;
        }

        if (!TimeOnly.TryParse(task.DeadlineLocalTime, out var time))
        {
            return DateOnly.FromDateTime(localNow.DateTime) > date;
        }

        var deadlineLocal = date.ToDateTime(time);
        var deadlineOffset = new DateTimeOffset(deadlineLocal, tz.GetUtcOffset(deadlineLocal));
        return utcNow > deadlineOffset.ToUniversalTime();
    }

    public static string? CompactPreview(TaskItem task)
    {
        if (string.IsNullOrWhiteSpace(task.DeadlineLocalDate))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(task.DeadlineLocalTime)
            ? task.DeadlineLocalDate
            : $"{task.DeadlineLocalDate} {task.DeadlineLocalTime}";
    }

    public static int CompareDeadlines(TaskItem a, TaskItem b)
    {
        var aHas = !string.IsNullOrWhiteSpace(a.DeadlineLocalDate);
        var bHas = !string.IsNullOrWhiteSpace(b.DeadlineLocalDate);
        if (aHas != bHas)
        {
            return aHas ? -1 : 1; // dated before undated
        }

        if (!aHas)
        {
            return 0;
        }

        var dateCmp = string.CompareOrdinal(a.DeadlineLocalDate, b.DeadlineLocalDate);
        if (dateCmp != 0)
        {
            return dateCmp;
        }

        var aTime = a.DeadlineLocalTime ?? "99:99";
        var bTime = b.DeadlineLocalTime ?? "99:99";
        return string.CompareOrdinal(aTime, bTime);
    }

    private static TimeZoneInfo ResolveTimeZone(string? id, TimeZoneInfo? fallback)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // fall through
            }
            catch (InvalidTimeZoneException)
            {
                // fall through
            }
        }

        return fallback ?? TimeZoneInfo.Local;
    }
}
