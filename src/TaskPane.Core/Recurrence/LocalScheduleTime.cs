namespace TaskPane.Core.Recurrence;

/// <summary>
/// Converts local calendar schedule times to UTC with explicit DST policy:
/// non-existent local times roll forward to the first valid instant;
/// ambiguous local times use the standard (earlier) offset.
/// </summary>
public static class LocalScheduleTime
{
    public static DateTimeOffset ToUtc(DateOnly localDate, TimeOnly? localTime, TimeZoneInfo timeZone)
    {
        var time = localTime ?? TimeOnly.MinValue;
        var localUnspecified = localDate.ToDateTime(time, DateTimeKind.Unspecified);

        if (timeZone.IsInvalidTime(localUnspecified))
        {
            // Spring forward: roll forward in 15-minute steps to first valid local time.
            var candidate = localUnspecified;
            for (var i = 0; i < 8; i++)
            {
                candidate = candidate.AddMinutes(15);
                if (!timeZone.IsInvalidTime(candidate))
                {
                    localUnspecified = candidate;
                    break;
                }
            }
        }

        if (timeZone.IsAmbiguousTime(localUnspecified))
        {
            var offsets = timeZone.GetAmbiguousTimeOffsets(localUnspecified);

            // Prefer the standard (non-daylight) offset.
            foreach (var offset in offsets.OrderBy(o => o.TotalMinutes))
            {
                var asUtc = new DateTimeOffset(localUnspecified, offset).ToUniversalTime();
                if (!timeZone.IsDaylightSavingTime(asUtc.UtcDateTime))
                {
                    return asUtc;
                }
            }

            // Fallback: earlier absolute instant (first occurrence chronologically).
            var earliest = offsets.MinBy(o => new DateTimeOffset(localUnspecified, o).ToUniversalTime());
            return new DateTimeOffset(localUnspecified, earliest).ToUniversalTime();
        }

        var utcOffset = timeZone.GetUtcOffset(localUnspecified);
        return new DateTimeOffset(localUnspecified, utcOffset).ToUniversalTime();
    }

    public static TimeZoneInfo Resolve(string? timeZoneId, TimeZoneInfo? fallback = null)
    {
        if (!string.IsNullOrWhiteSpace(timeZoneId))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
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
