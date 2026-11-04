using TaskPane.Core.Deadline;
using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using TaskPane.Core.Ids;
using TaskPane.Core.Sleep;

namespace TaskPane.Core.Tests;

public sealed class DeadlineAndSleepTests
{
    [Fact]
    public void Date_only_deadline_is_overdue_after_local_day()
    {
        var task = new TaskItem
        {
            Id = TaskId.New(),
            Title = "Due",
            DeadlineLocalDate = "2026-11-03",
            DeadlineTimeZoneId = "UTC",
            Status = TaskLifecycleStatus.Open
        };

        Assert.False(DeadlineEvaluator.IsOverdue(task, DateTimeOffset.Parse("2026-11-03T23:00:00Z"), TimeZoneInfo.Utc));
        Assert.True(DeadlineEvaluator.IsOverdue(task, DateTimeOffset.Parse("2026-11-04T00:00:01Z"), TimeZoneInfo.Utc));
    }

    [Fact]
    public void Timed_deadline_uses_local_clock_not_midnight_coercion_for_date_only_peers()
    {
        var dated = new TaskItem
        {
            Id = TaskId.New(),
            Title = "Date only",
            DeadlineLocalDate = "2026-11-04",
            Status = TaskLifecycleStatus.Open
        };
        var timed = new TaskItem
        {
            Id = TaskId.New(),
            Title = "Timed",
            DeadlineLocalDate = "2026-11-04",
            DeadlineLocalTime = "15:30",
            DeadlineTimeZoneId = "UTC",
            Status = TaskLifecycleStatus.Open
        };

        Assert.True(DeadlineEvaluator.CompareDeadlines(timed, dated) < 0);
        Assert.False(DeadlineEvaluator.IsOverdue(dated, DateTimeOffset.Parse("2026-11-04T20:00:00Z"), TimeZoneInfo.Utc));
        Assert.True(DeadlineEvaluator.IsOverdue(timed, DateTimeOffset.Parse("2026-11-04T16:00:00Z"), TimeZoneInfo.Utc));
    }

    [Fact]
    public void Sleep_presets_resolve_and_parent_hides_children()
    {
        var now = DateTimeOffset.Parse("2026-11-04T12:00:00Z");
        var until = SleepEvaluator.ResolveWakeUtc(SleepPreset.OneHour, now, TimeZoneInfo.Utc);
        Assert.Equal(now.AddHours(1), until);

        var parent = new TaskItem
        {
            Id = TaskId.New(),
            Title = "Parent",
            SleepUntilUtc = now.AddHours(2)
        };
        var child = new TaskItem
        {
            Id = TaskId.New(),
            Title = "Child",
            ParentTaskId = parent.Id
        };

        Assert.True(SleepEvaluator.IsHiddenBySleep(child, parent, now));
        Assert.False(SleepEvaluator.IsCurrentlySleeping(child, now));
    }
}
