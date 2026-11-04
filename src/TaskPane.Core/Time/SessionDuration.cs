using TaskPane.Core.Entities;

namespace TaskPane.Core.Time;

public static class SessionDuration
{
    public static TimeSpan DirectTotal(IEnumerable<TimeSession> sessions, DateTimeOffset? utcNow = null)
    {
        var now = utcNow ?? DateTimeOffset.UtcNow;
        var total = TimeSpan.Zero;
        foreach (var session in sessions)
        {
            if (session.IsDiscarded)
            {
                continue;
            }

            var end = session.EndedUtc ?? now;
            var span = end - session.StartedUtc;
            if (span > TimeSpan.Zero)
            {
                total += span;
            }
        }

        return total;
    }

    /// <summary>
    /// Parent rolled-up total = parent direct + each child's direct (no grandchild nesting).
    /// </summary>
    public static TimeSpan RolledUpTotal(
        IEnumerable<TimeSession> parentSessions,
        IEnumerable<IEnumerable<TimeSession>> childSessionGroups,
        DateTimeOffset? utcNow = null)
    {
        var total = DirectTotal(parentSessions, utcNow);
        foreach (var childSessions in childSessionGroups)
        {
            total += DirectTotal(childSessions, utcNow);
        }

        return total;
    }

    public static string FormatCompact(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        var totalMinutes = (int)Math.Floor(span.TotalMinutes);
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        if (hours > 0)
        {
            return $"{hours}h {minutes}m";
        }

        return $"{minutes}m";
    }
}
