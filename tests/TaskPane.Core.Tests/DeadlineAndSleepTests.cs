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

    [Fact]
    public void Timed_sleep_expires_without_mutating_deadline_or_priority()
    {
        var now = DateTimeOffset.Parse("2026-11-04T12:00:00Z");
        var task = new TaskItem
        {
            Id = TaskId.New(),
            Title = "Nap",
            Priority = TaskPriority.High,
            DeadlineLocalDate = "2026-12-01",
            SleepUntilUtc = now.AddMinutes(30)
        };

        Assert.True(SleepEvaluator.IsCurrentlySleeping(task, now));
        Assert.False(SleepEvaluator.HasExpired(task, now));

        var after = now.AddHours(1);
        Assert.False(SleepEvaluator.IsCurrentlySleeping(task, after));
        Assert.True(SleepEvaluator.HasExpired(task, after));
        Assert.Equal(TaskPriority.High, task.Priority);
        Assert.Equal("2026-12-01", task.DeadlineLocalDate);
        Assert.Equal(now.AddMinutes(30), task.SleepUntilUtc);
    }

    [Fact]
    public void Custom_and_named_presets_resolve_wake_times()
    {
        var now = DateTimeOffset.Parse("2026-11-04T12:00:00Z");
        var custom = DateTimeOffset.Parse("2026-11-05T18:30:00Z");
        Assert.Equal(custom, SleepEvaluator.ResolveWakeUtc(SleepPreset.Custom, now, TimeZoneInfo.Utc, custom));
        Assert.Equal(now.AddDays(7), SleepEvaluator.ResolveWakeUtc(SleepPreset.NextWeek, now, TimeZoneInfo.Utc));
        Assert.Null(SleepEvaluator.ResolveWakeUtc(SleepPreset.Indefinitely, now, TimeZoneInfo.Utc));

        var morning = SleepEvaluator.ResolveWakeUtc(SleepPreset.TomorrowMorning, now, TimeZoneInfo.Utc);
        Assert.NotNull(morning);
        var localMorning = TimeZoneInfo.ConvertTime(morning!.Value, TimeZoneInfo.Utc);
        Assert.Equal(9, localMorning.Hour);
        Assert.Equal(0, localMorning.Minute);
        Assert.Equal(new DateOnly(2026, 11, 5), DateOnly.FromDateTime(localMorning.DateTime));
    }

    [Fact]
    public void Child_sleep_is_independent_of_parent_visibility()
    {
        var now = DateTimeOffset.Parse("2026-11-04T12:00:00Z");
        var parent = new TaskItem { Id = TaskId.New(), Title = "Parent" };
        var sleepingChild = new TaskItem
        {
            Id = TaskId.New(),
            Title = "Child",
            ParentTaskId = parent.Id,
            SleepUntilUtc = now.AddHours(3)
        };
        var awakeSibling = new TaskItem
        {
            Id = TaskId.New(),
            Title = "Sibling",
            ParentTaskId = parent.Id
        };

        Assert.True(SleepEvaluator.IsCurrentlySleeping(sleepingChild, now));
        Assert.True(SleepEvaluator.IsHiddenBySleep(sleepingChild, parent, now));
        Assert.False(SleepEvaluator.IsHiddenBySleep(awakeSibling, parent, now));
        Assert.False(SleepEvaluator.IsCurrentlySleeping(parent, now));
    }

    [Fact]
    public void Indefinite_sleep_never_auto_expires_and_formats_wake_label()
    {
        var now = DateTimeOffset.Parse("2026-11-04T12:00:00Z");
        var task = new TaskItem
        {
            Id = TaskId.New(),
            Title = "Parked",
            SleepIndefinitely = true
        };

        Assert.True(SleepEvaluator.IsCurrentlySleeping(task, now.AddYears(1)));
        Assert.False(SleepEvaluator.HasExpired(task, now.AddYears(1)));
        Assert.Equal("Until woken", SleepEvaluator.FormatWakeLabel(task, now, TimeZoneInfo.Utc));

        task.SleepIndefinitely = false;
        task.SleepUntilUtc = now.AddHours(2);
        var label = SleepEvaluator.FormatWakeLabel(task, now, TimeZoneInfo.Utc);
        Assert.StartsWith("Until ", label);
        Assert.DoesNotContain("tomorrow", label, StringComparison.OrdinalIgnoreCase);
    }
}
