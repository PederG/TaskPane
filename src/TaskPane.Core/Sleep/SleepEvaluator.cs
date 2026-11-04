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

    private static DateTimeOffset TomorrowMorningUtc(DateTimeOffset utcNow, TimeZoneInfo localTz)
    {
        var local = TimeZoneInfo.ConvertTime(utcNow, localTz);
        var tomorrow = DateOnly.FromDateTime(local.DateTime).AddDays(1);
        var localMorning = tomorrow.ToDateTime(new TimeOnly(9, 0));
        var offset = localTz.GetUtcOffset(localMorning);
        return new DateTimeOffset(localMorning, offset).ToUniversalTime();
    }
}
