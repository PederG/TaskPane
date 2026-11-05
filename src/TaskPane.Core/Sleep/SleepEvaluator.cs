using System.Globalization;
using TaskPane.Core.Entities;
using TaskPane.Core.Enums;

namespace TaskPane.Core.Sleep;

public static class SleepEvaluator
{
    public static bool IsCurrentlySleeping(TaskItem task, DateTimeOffset utcNow)
    {
        if (task.IsDeleted)
        {
            return false;
        }

        if (task.SleepIndefinitely)
        {
            return true;
        }

        return task.SleepUntilUtc is { } until && until > utcNow;
    }

    /// <summary>
    /// Overlay hides a child when the child itself sleeps or its parent sleeps.
    /// </summary>
    public static bool IsHiddenBySleep(TaskItem task, TaskItem? parent, DateTimeOffset utcNow)
        => IsCurrentlySleeping(task, utcNow)
           || (parent is not null && IsCurrentlySleeping(parent, utcNow));

    /// <summary>
    /// True when a timed sleep has passed and the task should reappear without an explicit wake.
    /// Indefinite sleep never auto-expires.
    /// </summary>
    public static bool HasExpired(TaskItem task, DateTimeOffset utcNow)
        => !task.SleepIndefinitely
           && task.SleepUntilUtc is { } until
           && until <= utcNow;

    public static DateTimeOffset? ResolveWakeUtc(
        SleepPreset preset,
        DateTimeOffset utcNow,
        TimeZoneInfo localTz,
        DateTimeOffset? customUntilUtc = null)
    {
        return preset switch
        {
            SleepPreset.OneHour => utcNow.AddHours(1),
            SleepPreset.TomorrowMorning => TomorrowMorningUtc(utcNow, localTz),
            SleepPreset.NextWeek => utcNow.AddDays(7),
            SleepPreset.Indefinitely => null,
            SleepPreset.Custom => customUntilUtc
                ?? throw new ArgumentException("Custom sleep requires an until timestamp.", nameof(customUntilUtc)),
            _ => throw new ArgumentOutOfRangeException(nameof(preset))
        };
    }

    /// <summary>
    /// Compact wake label for sleeping lists (local wall clock). Empty when not sleeping.
    /// </summary>
    public static string FormatWakeLabel(
        TaskItem task,
        DateTimeOffset utcNow,
        TimeZoneInfo? localTz = null)
    {
        if (!IsCurrentlySleeping(task, utcNow))
        {
            return string.Empty;
        }

        if (task.SleepIndefinitely)
        {
            return "Until woken";
        }

        if (task.SleepUntilUtc is not { } until)
        {
            return "Until woken";
        }

        var tz = localTz ?? TimeZoneInfo.Local;
        var local = TimeZoneInfo.ConvertTime(until, tz);
        var localNow = TimeZoneInfo.ConvertTime(utcNow, tz);
        if (local.Date == localNow.Date)
        {
            return $"Until {local.ToString("t", CultureInfo.CurrentCulture)}";
        }

        if (local.Date == localNow.Date.AddDays(1))
        {
            return $"Until tomorrow {local.ToString("t", CultureInfo.CurrentCulture)}";
        }

        return $"Until {local.ToString("g", CultureInfo.CurrentCulture)}";
    }

    private static DateTimeOffset TomorrowMorningUtc(DateTimeOffset utcNow, TimeZoneInfo localTz)
    {
        var local = TimeZoneInfo.ConvertTime(utcNow, localTz);
        var tomorrow = DateOnly.FromDateTime(local.DateTime).AddDays(1);
        var localMorning = tomorrow.ToDateTime(new TimeOnly(9, 0));
        var offset = localTz.GetUtcOffset(localMorning);
        return new DateTimeOffset(localMorning, offset).ToUniversalTime();
    }
}
