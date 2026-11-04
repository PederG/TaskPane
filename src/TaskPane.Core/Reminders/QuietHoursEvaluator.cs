using TaskPane.Core.Entities;

namespace TaskPane.Core.Reminders;

public static class QuietHoursEvaluator
{
    public static bool IsInQuietHours(
        ReminderSettings settings,
        DateTimeOffset utcNow,
        TimeZoneInfo? localTz = null)
    {
        if (!settings.QuietHoursEnabled)
        {
            return false;
        }

        if (!TimeOnly.TryParse(settings.QuietStartLocal, out var start)
            || !TimeOnly.TryParse(settings.QuietEndLocal, out var end))
        {
            return false;
        }

        var tz = localTz ?? TimeZoneInfo.Local;
        var local = TimeZoneInfo.ConvertTime(utcNow, tz);
        var nowTime = TimeOnly.FromDateTime(local.DateTime);

        // Window that does not cross midnight.
        if (start == end)
        {
            return false;
        }

        if (start < end)
        {
            return nowTime >= start && nowTime < end;
        }

        // Spans midnight (e.g. 22:00–07:00).
        return nowTime >= start || nowTime < end;
    }
}
