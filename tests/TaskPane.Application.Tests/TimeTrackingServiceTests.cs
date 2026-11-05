using Microsoft.Extensions.Logging.Abstractions;
using TaskPane.Application.Models;
using TaskPane.Application.Services;
using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using TaskPane.Core.Errors;
using TaskPane.Core.Ids;
using TaskPane.Core.Interfaces;

namespace TaskPane.Application.Tests;

public sealed class TimeTrackingServiceTests
{
    [Fact]
    public async Task Rapid_switch_creates_non_overlapping_sessions()
    {
        var harness = CreateHarness();
        var a = await harness.Tasks.CreateAsync(new CreateTaskRequest { Title = "A" });
        var b = await harness.Tasks.CreateAsync(new CreateTaskRequest { Title = "B" });

        await harness.Time.StartOrSwitchAsync(a.Id);
        harness.Clock.UtcNow = harness.Clock.UtcNow.AddMinutes(10);
        await harness.Time.StartOrSwitchAsync(b.Id);
        harness.Clock.UtcNow = harness.Clock.UtcNow.AddMinutes(5);
        await harness.Time.StopAsync();

        var sessions = harness.Sessions.Items.OrderBy(s => s.StartedUtc).ToList();
        Assert.Equal(2, sessions.Count);
        Assert.Equal(SessionEndReason.Switched, sessions[0].EndReason);
        Assert.Equal(SessionEndReason.Stopped, sessions[1].EndReason);
        Assert.True(sessions[0].EndedUtc <= sessions[1].StartedUtc);
        Assert.Null(await harness.Time.GetActiveSessionAsync());
        Assert.Equal(TimeSpan.FromMinutes(10), sessions[0].Duration);
        Assert.Equal(TimeSpan.FromMinutes(5), sessions[1].Duration);
    }

    [Fact]
    public async Task Restart_recovery_discard_charges_no_offline_time()
    {
        var harness = CreateHarness();
        var task = await harness.Tasks.CreateAsync(new CreateTaskRequest { Title = "Recover me" });
        await harness.Time.StartOrSwitchAsync(task.Id);
        harness.Clock.UtcNow = harness.Clock.UtcNow.AddHours(8);

        var recovered = await harness.Time.RecoverOpenSessionAsync(SessionRecoveryChoice.Discard);
        Assert.NotNull(recovered);
        Assert.True(recovered!.IsDiscarded);
        Assert.Equal(SessionEndReason.Recovered, recovered.EndReason);
        Assert.Null(await harness.Time.GetActiveSessionAsync());

        var totals = await harness.Time.GetTotalsAsync(task.Id);
        Assert.Equal(TimeSpan.Zero, totals.Direct);
    }

    [Fact]
    public async Task Restart_recovery_close_at_known_time_avoids_overnight_gap()
    {
        var harness = CreateHarness();
        var start = harness.Clock.UtcNow;
        var task = await harness.Tasks.CreateAsync(new CreateTaskRequest { Title = "Known close" });
        await harness.Time.StartOrSwitchAsync(task.Id);
        var known = start.AddMinutes(12);
        harness.Clock.UtcNow = start.AddHours(10);

        await harness.Time.RecoverOpenSessionAsync(SessionRecoveryChoice.CloseAtKnownTime, known);
        var sessions = await harness.Time.ListSessionsForTaskAsync(task.Id);
        Assert.Single(sessions);
        Assert.Equal(TimeSpan.FromMinutes(12), sessions[0].Duration);
    }

    [Fact]
    public async Task Parent_rollup_sums_direct_child_time_without_double_count()
    {
        var harness = CreateHarness();
        var parent = await harness.Tasks.CreateAsync(new CreateTaskRequest { Title = "Parent" });
        var child = await harness.Tasks.CreateAsync(new CreateTaskRequest
        {
            Title = "Child",
            ParentTaskId = parent.Id
        });

        await harness.Time.StartOrSwitchAsync(parent.Id);
        harness.Clock.UtcNow = harness.Clock.UtcNow.AddMinutes(20);
        await harness.Time.StartOrSwitchAsync(child.Id);
        harness.Clock.UtcNow = harness.Clock.UtcNow.AddMinutes(15);
        await harness.Time.StopAsync();

        var parentTotals = await harness.Time.GetTotalsAsync(parent.Id);
        var childTotals = await harness.Time.GetTotalsAsync(child.Id);
        Assert.Equal(TimeSpan.FromMinutes(20), parentTotals.Direct);
        Assert.Equal(TimeSpan.FromMinutes(15), childTotals.Direct);
        Assert.Equal(TimeSpan.FromMinutes(35), parentTotals.RolledUp);
        Assert.Equal(childTotals.Direct, childTotals.RolledUp);
    }

    [Fact]
    public async Task Manual_adjustment_updates_duration()
    {
        var harness = CreateHarness();
        var task = await harness.Tasks.CreateAsync(new CreateTaskRequest { Title = "Adjust" });
        await harness.Time.StartOrSwitchAsync(task.Id);
        harness.Clock.UtcNow = harness.Clock.UtcNow.AddMinutes(40);
        var stopped = await harness.Time.StopAsync();
        Assert.NotNull(stopped);

        var start = stopped!.StartedUtc;
        await harness.Time.AdjustSessionAsync(new SessionEditRequest
        {
            Id = stopped.Id,
            StartedUtc = start,
            EndedUtc = start.AddMinutes(25),
            IsDiscarded = false
        });

        var totals = await harness.Time.GetTotalsAsync(task.Id);
        Assert.Equal(TimeSpan.FromMinutes(25), totals.Direct);
    }

    [Fact]
    public async Task Complete_and_sleep_end_active_session()
    {
        var harness = CreateHarness();
        var task = await harness.Tasks.CreateAsync(new CreateTaskRequest { Title = "Active" });
        await harness.Time.StartOrSwitchAsync(task.Id);
        harness.Clock.UtcNow = harness.Clock.UtcNow.AddMinutes(3);
        await harness.Tasks.CompleteAsync(task.Id, null);
        Assert.Null(await harness.Time.GetActiveSessionAsync());
        var completed = (await harness.Time.ListSessionsForTaskAsync(task.Id)).Single();
        Assert.Equal(SessionEndReason.Completed, completed.EndReason);

        var other = await harness.Tasks.CreateAsync(new CreateTaskRequest { Title = "Sleeping" });
        await harness.Time.StartOrSwitchAsync(other.Id);
        await harness.Tasks.SleepAsync(new SleepTaskRequest { Id = other.Id, Preset = SleepPreset.OneHour });
        Assert.Null(await harness.Time.GetActiveSessionAsync());
        Assert.Equal(SessionEndReason.Slept, (await harness.Time.ListSessionsForTaskAsync(other.Id)).Single().EndReason);
    }

    [Fact]
    public async Task Sleeping_parent_stops_descendant_active_timer_with_Slept_and_preserves_time()
    {
        var harness = CreateHarness();
        var parent = await harness.Tasks.CreateAsync(new CreateTaskRequest { Title = "Parent" });
        var child = await harness.Tasks.CreateAsync(new CreateTaskRequest
        {
            Title = "Child",
            ParentTaskId = parent.Id
        });

        await harness.Time.StartOrSwitchAsync(child.Id);
        harness.Clock.UtcNow = harness.Clock.UtcNow.AddMinutes(12);

        await harness.Tasks.SleepAsync(new SleepTaskRequest { Id = parent.Id, Preset = SleepPreset.OneHour });

        Assert.Null(await harness.Time.GetActiveSessionAsync());
        var session = (await harness.Time.ListSessionsForTaskAsync(child.Id)).Single();
        Assert.Equal(SessionEndReason.Slept, session.EndReason);
        Assert.Equal(TimeSpan.FromMinutes(12), session.Duration);
        Assert.False(session.IsDiscarded);

        var totals = await harness.Time.GetTotalsAsync(child.Id);
        Assert.Equal(TimeSpan.FromMinutes(12), totals.Direct);
    }

    [Fact]
    public async Task Sleeping_subtask_does_not_stop_unrelated_active_timer()
    {
        var harness = CreateHarness();
        var parent = await harness.Tasks.CreateAsync(new CreateTaskRequest { Title = "Parent" });
        var childA = await harness.Tasks.CreateAsync(new CreateTaskRequest
        {
            Title = "A",
            ParentTaskId = parent.Id
        });
        var childB = await harness.Tasks.CreateAsync(new CreateTaskRequest
        {
            Title = "B",
            ParentTaskId = parent.Id
        });
        var unrelated = await harness.Tasks.CreateAsync(new CreateTaskRequest { Title = "Other root" });

        await harness.Time.StartOrSwitchAsync(childB.Id);
        await harness.Tasks.SleepAsync(new SleepTaskRequest { Id = childA.Id, Preset = SleepPreset.OneHour });

        var active = await harness.Time.GetActiveSessionAsync();
        Assert.NotNull(active);
        Assert.Equal(childB.Id, active!.TaskId);
        Assert.Empty(await harness.Time.ListSessionsForTaskAsync(childA.Id));

        await harness.Time.StartOrSwitchAsync(unrelated.Id);
        await harness.Tasks.SleepAsync(new SleepTaskRequest { Id = childB.Id, Preset = SleepPreset.OneHour });
        active = await harness.Time.GetActiveSessionAsync();
        Assert.NotNull(active);
        Assert.Equal(unrelated.Id, active!.TaskId);
    }

    [Fact]
    public async Task Wake_after_sleep_does_not_restart_timer()
    {
        var harness = CreateHarness();
        var task = await harness.Tasks.CreateAsync(new CreateTaskRequest { Title = "Timed" });
        await harness.Time.StartOrSwitchAsync(task.Id);
        harness.Clock.UtcNow = harness.Clock.UtcNow.AddMinutes(5);
        await harness.Tasks.SleepAsync(new SleepTaskRequest { Id = task.Id, Preset = SleepPreset.OneHour });

        Assert.Null(await harness.Time.GetActiveSessionAsync());
        await harness.Tasks.WakeAsync(task.Id);

        Assert.Null(await harness.Time.GetActiveSessionAsync());
        Assert.Single(await harness.Time.ListSessionsForTaskAsync(task.Id));
        Assert.Equal(SessionEndReason.Slept, (await harness.Time.ListSessionsForTaskAsync(task.Id)).Single().EndReason);
    }

    [Fact]
    public async Task Lock_pause_remembers_resume_target()
    {
        var harness = CreateHarness();
        var task = await harness.Tasks.CreateAsync(new CreateTaskRequest { Title = "Lock" });
        await harness.Time.StartOrSwitchAsync(task.Id);
        await harness.Time.EndActiveIfAnyAsync(SessionEndReason.Locked, rememberForResume: true);

        var settings = await harness.Time.GetSettingsAsync();
        Assert.Equal(task.Id.ToString(), settings.PendingResumeTaskId);
        Assert.Equal(nameof(SessionEndReason.Locked), settings.PendingResumeReason);
        Assert.Null(await harness.Time.GetActiveSessionAsync());

        await harness.Time.ResumePendingAsync();
        var active = await harness.Time.GetActiveSessionAsync();
        Assert.NotNull(active);
        Assert.Equal(task.Id, active!.TaskId);
    }

    [Fact]
    public async Task Idle_discard_stops_at_idle_onset()
    {
        var harness = CreateHarness();
        var task = await harness.Tasks.CreateAsync(new CreateTaskRequest { Title = "Idle" });
        var start = harness.Clock.UtcNow;
        await harness.Time.StartOrSwitchAsync(task.Id);
        harness.Clock.UtcNow = start.AddMinutes(30);
        await harness.Time.HandleIdleAsync(TimeSpan.FromMinutes(12), IdleResolutionChoice.DiscardIdle);

        var session = (await harness.Time.ListSessionsForTaskAsync(task.Id)).Single();
        Assert.Equal(SessionEndReason.Idle, session.EndReason);
        Assert.Equal(TimeSpan.FromMinutes(18), session.Duration);
        Assert.Null(await harness.Time.GetActiveSessionAsync());
    }

    [Fact]
    public async Task Idle_keep_and_resume_closes_at_idle_onset_and_starts_fresh_session()
    {
        var harness = CreateHarness();
        var task = await harness.Tasks.CreateAsync(new CreateTaskRequest { Title = "Idle resume" });
        var start = harness.Clock.UtcNow;
        await harness.Time.StartOrSwitchAsync(task.Id);
        harness.Clock.UtcNow = start.AddMinutes(30);
        await harness.Time.HandleIdleAsync(TimeSpan.FromMinutes(12), IdleResolutionChoice.KeepAndResume);

        var sessions = (await harness.Time.ListSessionsForTaskAsync(task.Id)).OrderBy(s => s.StartedUtc).ToList();
        Assert.Equal(2, sessions.Count);
        Assert.Equal(SessionEndReason.Idle, sessions[0].EndReason);
        Assert.Equal(TimeSpan.FromMinutes(18), sessions[0].Duration);
        Assert.True(sessions[1].IsOpen);
        Assert.Equal(harness.Clock.UtcNow, sessions[1].StartedUtc);
        Assert.Equal(TimeSpan.FromMinutes(18), (await harness.Time.GetTotalsAsync(task.Id)).Direct);

        var settings = await harness.Time.GetSettingsAsync();
        Assert.Null(settings.PendingResumeTaskId);
    }

    [Fact]
    public async Task Soft_delete_parent_stops_descendant_timer_with_Deleted_and_preserves_time()
    {
        var harness = CreateHarness();
        var parent = await harness.Tasks.CreateAsync(new CreateTaskRequest { Title = "Parent" });
        var child = await harness.Tasks.CreateAsync(new CreateTaskRequest
        {
            Title = "Child",
            ParentTaskId = parent.Id
        });

        await harness.Time.StartOrSwitchAsync(child.Id);
        harness.Clock.UtcNow = harness.Clock.UtcNow.AddMinutes(9);

        await harness.Tasks.SoftDeleteAsync(parent.Id);

        Assert.Null(await harness.Time.GetActiveSessionAsync());
        var session = (await harness.Time.ListSessionsForTaskAsync(child.Id)).Single();
        Assert.Equal(SessionEndReason.Deleted, session.EndReason);
        Assert.Equal(TimeSpan.FromMinutes(9), session.Duration);
        Assert.False(session.IsDiscarded);
        // Child is soft-deleted with the parent cascade; totals API requires a live task.
        Assert.Equal(TimeSpan.FromMinutes(9), session.EndedUtc!.Value - session.StartedUtc);
    }

    [Fact]
    public async Task Toggle_same_task_stops_without_completing()
    {
        var harness = CreateHarness();
        var task = await harness.Tasks.CreateAsync(new CreateTaskRequest { Title = "Toggle" });
        await harness.Time.ToggleAsync(task.Id);
        Assert.NotNull(await harness.Time.GetActiveSessionAsync());
        await harness.Time.ToggleAsync(task.Id);
        Assert.Null(await harness.Time.GetActiveSessionAsync());
        var reloaded = await harness.Tasks.GetAsync(task.Id);
        Assert.False(reloaded!.IsCompleted);
    }

    [Fact]
    public async Task Invalid_manual_range_rejected()
    {
        var harness = CreateHarness();
        var task = await harness.Tasks.CreateAsync(new CreateTaskRequest { Title = "Bad" });
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            harness.Time.AddManualSessionAsync(new ManualSessionRequest
            {
                TaskId = task.Id,
                StartedUtc = harness.Clock.UtcNow,
                EndedUtc = harness.Clock.UtcNow.AddMinutes(-5)
            }));
        Assert.Equal(ValidationCodes.InvalidSessionRange, ex.Code);
    }

    private static Harness CreateHarness()
    {
        var tasks = new InMemoryTaskRepository();
        var tags = new InMemoryTagRepository();
        var projects = new InMemoryProjectRepository();
        var deps = new InMemoryDependencyRepository();
        var audit = new InMemoryAuditRepository();
        var sessions = new InMemoryTimeSessionRepository();
        var settings = new InMemorySettingsRepository();
        var uow = new PassthroughUnitOfWork();
        var clock = new FixedClock(DateTimeOffset.Parse("2026-11-04T08:00:00Z"));
        var focus = new FocusService(tasks, tags, deps);
        var time = new TimeTrackingService(
            sessions,
            tasks,
            settings,
            uow,
            clock,
            NullLogger<TimeTrackingService>.Instance);
        var history = new HistoryService(audit, tasks, deps, tags, uow, NullLogger<HistoryService>.Instance);
        var recurrence = new RecurrenceService(
            new InMemoryRecurrenceTemplateRepository(),
            tasks,
            uow,
            clock,
            NullLogger<RecurrenceService>.Instance);
        var taskService = new TaskService(
            tasks,
            tags,
            projects,
            uow,
            focus,
            time,
            history,
            recurrence,
            NullLogger<TaskService>.Instance);
        return new Harness(taskService, time, sessions, clock);
    }

    private sealed record Harness(
        TaskService Tasks,
        TimeTrackingService Time,
        InMemoryTimeSessionRepository Sessions,
        FixedClock Clock);

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class PassthroughUnitOfWork : IUnitOfWork
    {
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
            => action(cancellationToken);
    }

    private sealed class InMemorySettingsRepository : ISettingsRepository
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public Task<OverlayPlacement> GetOverlayPlacementAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new OverlayPlacement());

        public Task SaveOverlayPlacementAsync(OverlayPlacement placement, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<string?> GetRawAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(_values.TryGetValue(key, out var v) ? v : null);

        public Task SetRawAsync(string key, string jsonValue, CancellationToken cancellationToken = default)
        {
            _values[key] = jsonValue;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryTimeSessionRepository : ITimeSessionRepository
    {
        public List<TimeSession> Items { get; } = [];

        public Task<TimeSession?> GetByIdAsync(SessionId id, CancellationToken cancellationToken = default)
            => Task.FromResult(Items.FirstOrDefault(s => s.Id.Equals(id)));

        public Task<TimeSession?> GetOpenSessionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Items.FirstOrDefault(s => s.IsOpen));

        public Task<IReadOnlyList<TimeSession>> ListForTaskAsync(
            TaskId taskId,
            bool includeDiscarded = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TimeSession>>(
                Items.Where(s => s.TaskId.Equals(taskId) && (includeDiscarded || !s.IsDiscarded)).ToList());

        public Task<IReadOnlyList<TimeSession>> ListForTasksAsync(
            IReadOnlyList<TaskId> taskIds,
            bool includeDiscarded = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TimeSession>>(
                Items.Where(s => taskIds.Contains(s.TaskId) && (includeDiscarded || !s.IsDiscarded)).ToList());

        public Task<IReadOnlyList<TimeSession>> ListInRangeAsync(
            DateTimeOffset rangeStartUtc,
            DateTimeOffset rangeEndUtc,
            bool includeDiscarded = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TimeSession>>(
                Items.Where(s =>
                    (includeDiscarded || !s.IsDiscarded)
                    && s.StartedUtc < rangeEndUtc
                    && (s.EndedUtc is null || s.EndedUtc > rangeStartUtc)).ToList());

        public Task<IReadOnlyList<TimeSession>> ListAllAsync(
            bool includeDiscarded = true,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TimeSession>>(
                Items.Where(s => includeDiscarded || !s.IsDiscarded).ToList());

        public Task InsertAsync(TimeSession session, CancellationToken cancellationToken = default)
        {
            if (session.OpenSlot == 1 && Items.Any(s => s.OpenSlot == 1 && s.EndedUtc is null))
            {
                throw new InvalidOperationException("Only one open session allowed.");
            }

            Items.Add(Clone(session));
            return Task.CompletedTask;
        }

        public Task UpdateAsync(TimeSession session, CancellationToken cancellationToken = default)
        {
            var idx = Items.FindIndex(s => s.Id.Equals(session.Id));
            Items[idx] = Clone(session);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(SessionId id, CancellationToken cancellationToken = default)
        {
            Items.RemoveAll(s => s.Id.Equals(id));
            return Task.CompletedTask;
        }

        private static TimeSession Clone(TimeSession s) => new()
        {
            Id = s.Id,
            TaskId = s.TaskId,
            StartedUtc = s.StartedUtc,
            EndedUtc = s.EndedUtc,
            EndReason = s.EndReason,
            IsDiscarded = s.IsDiscarded,
            OpenSlot = s.OpenSlot,
            CreatedUtc = s.CreatedUtc,
            UpdatedUtc = s.UpdatedUtc
        };
    }

    private sealed class InMemoryTaskRepository : ITaskRepository
    {
        public List<TaskItem> Items { get; } = [];

        public Task<TaskItem?> GetByIdAsync(TaskId id, CancellationToken cancellationToken = default)
            => Task.FromResult(Items.FirstOrDefault(t => t.Id.Equals(id)));

        public Task<IReadOnlyList<TaskItem>> ListAsync(bool includeDeleted = false, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TaskItem>>(Items.Where(t => includeDeleted || !t.IsDeleted).ToList());

        public Task<IReadOnlyList<TaskItem>> ListChildrenAsync(TaskId parentId, bool includeDeleted = false, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TaskItem>>(
                Items.Where(t => t.ParentTaskId.Equals(parentId) && (includeDeleted || !t.IsDeleted)).ToList());

        public Task InsertAsync(TaskItem task, CancellationToken cancellationToken = default)
        {
            Items.Add(Clone(task));
            return Task.CompletedTask;
        }

        public Task UpdateAsync(TaskItem task, CancellationToken cancellationToken = default)
        {
            var idx = Items.FindIndex(t => t.Id.Equals(task.Id));
            Items[idx] = Clone(task);
            return Task.CompletedTask;
        }

        public Task SoftDeleteBatchAsync(IReadOnlyList<TaskId> ids, Guid deletionBatchId, DateTimeOffset deletedUtc, CancellationToken cancellationToken = default)
        {
            foreach (var item in Items.Where(t => ids.Contains(t.Id)))
            {
                item.DeletedUtc = deletedUtc;
                item.DeletionBatchId = deletionBatchId;
                item.UpdatedUtc = deletedUtc;
                item.Revision++;
            }

            return Task.CompletedTask;
        }

        public Task RestoreBatchAsync(Guid deletionBatchId, CancellationToken cancellationToken = default)
        {
            foreach (var item in Items.Where(t => t.DeletionBatchId == deletionBatchId))
            {
                item.DeletedUtc = null;
                item.DeletionBatchId = null;
                item.UpdatedUtc = DateTimeOffset.UtcNow;
                item.Revision++;
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TaskItem>> ListByDeletionBatchAsync(Guid deletionBatchId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TaskItem>>(Items.Where(t => t.DeletionBatchId == deletionBatchId).ToList());

        public Task<double> GetNextSortKeyAsync(TaskId? parentId, FocusBucket? focusBucket, CancellationToken cancellationToken = default)
        {
            var query = Items.Where(t => !t.IsDeleted);
            query = parentId is { } pid
                ? query.Where(t => t.ParentTaskId.Equals(pid))
                : query.Where(t => t.IsRoot && (focusBucket is null || t.FocusBucket == focusBucket));
            var max = query.Select(t => t.ManualSortKey).DefaultIfEmpty(0).Max();
            return Task.FromResult(max + 1000);
        }

        public Task<TaskItem?> FindByOccurrenceKeyAsync(
            RecurrenceTemplateId templateId,
            string occurrenceKey,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Items.FirstOrDefault(t =>
                t.RecurrenceTemplateId.Equals(templateId)
                && string.Equals(t.OccurrenceKey, occurrenceKey, StringComparison.Ordinal)));

        public Task<IReadOnlyList<TaskItem>> ListByRecurrenceTemplateAsync(
            RecurrenceTemplateId templateId,
            bool includeDeleted = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TaskItem>>(
                Items.Where(t =>
                    t.RecurrenceTemplateId.Equals(templateId)
                    && (includeDeleted || !t.IsDeleted)).ToList());

        private static TaskItem Clone(TaskItem task) => new()

        {
            Id = task.Id,
            ParentTaskId = task.ParentTaskId,
            Title = task.Title,
            Notes = task.Notes,
            Priority = task.Priority,
            FocusBucket = task.FocusBucket,
            Status = task.Status,
            ManualSortKey = task.ManualSortKey,
            DeadlineLocalDate = task.DeadlineLocalDate,
            DeadlineLocalTime = task.DeadlineLocalTime,
            DeadlineTimeZoneId = task.DeadlineTimeZoneId,
            SleepUntilUtc = task.SleepUntilUtc,
            SleepIndefinitely = task.SleepIndefinitely,
            WaitingFor = task.WaitingFor,
            WaitingFollowUpUtc = task.WaitingFollowUpUtc,
            ProjectId = task.ProjectId,
            RecurrenceTemplateId = task.RecurrenceTemplateId,
            OccurrenceKey = task.OccurrenceKey,
            CreatedUtc = task.CreatedUtc,
            UpdatedUtc = task.UpdatedUtc,
            CompletedUtc = task.CompletedUtc,
            DeletedUtc = task.DeletedUtc,
            DeletionBatchId = task.DeletionBatchId,
            Revision = task.Revision
        };
    }

    private sealed class InMemoryTagRepository : ITagRepository
    {
        public Task<Tag?> GetByIdAsync(TagId id, CancellationToken cancellationToken = default)
            => Task.FromResult<Tag?>(null);

        public Task<Tag?> FindByNameAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult<Tag?>(null);

        public Task<IReadOnlyList<Tag>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Tag>>([]);

        public Task InsertAsync(Tag tag, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateAsync(Tag tag, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SetTaskTagsAsync(TaskId taskId, IReadOnlyList<TagId> tagIds, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<TagId>> GetTagIdsForTaskAsync(TaskId taskId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TagId>>([]);

        public Task<IReadOnlyDictionary<TaskId, IReadOnlyList<TagId>>> GetTagIdsForTasksAsync(
            IReadOnlyList<TaskId> taskIds,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<TaskId, IReadOnlyList<TagId>>>(
                taskIds.ToDictionary(id => id, _ => (IReadOnlyList<TagId>)[]));

        public Task<IReadOnlyList<(TaskId TaskId, TagId TagId)>> ListAllTaskTagsAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<(TaskId TaskId, TagId TagId)>>([]);
    }

    private sealed class InMemoryProjectRepository : IProjectRepository
    {
        public Task<Project?> GetByIdAsync(ProjectId id, CancellationToken cancellationToken = default)
            => Task.FromResult<Project?>(null);

        public Task<IReadOnlyList<Project>> ListAsync(bool includeArchived = false, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Project>>([]);

        public Task InsertAsync(Project project, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateAsync(Project project, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class InMemoryDependencyRepository : IDependencyRepository
    {
        public Task<DependencyEdge?> GetByIdAsync(DependencyId id, CancellationToken cancellationToken = default)
            => Task.FromResult<DependencyEdge?>(null);

        public Task<IReadOnlyList<DependencyEdge>> ListAsync(bool includeDeleted = false, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DependencyEdge>>([]);

        public Task<IReadOnlyList<DependencyEdge>> ListForTaskAsync(
            TaskId taskId,
            bool includeDeleted = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DependencyEdge>>([]);

        public Task<DependencyEdge?> FindEdgeAsync(
            TaskId predecessorId,
            TaskId successorId,
            DependencyKind kind,
            bool includeDeleted = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult<DependencyEdge?>(null);

        public Task InsertAsync(DependencyEdge edge, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateAsync(DependencyEdge edge, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SoftDeleteAsync(DependencyId id, DateTimeOffset deletedUtc, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class InMemoryAuditRepository : IAuditRepository
    {
        public Task InsertAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<AuditEvent?> GetByIdAsync(AuditEventId id, CancellationToken cancellationToken = default)
            => Task.FromResult<AuditEvent?>(null);

        public Task<IReadOnlyList<AuditEvent>> ListRecentAsync(int limit = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AuditEvent>>([]);

        public Task<IReadOnlyList<AuditEvent>> ListForEntityAsync(
            string entityType,
            string entityId,
            int limit = 50,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AuditEvent>>([]);

        public Task<AuditEvent?> GetLatestUndoableAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<AuditEvent?>(null);

        public Task<IReadOnlyList<AuditEvent>> ListAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AuditEvent>>([]);
    }

    private sealed class InMemoryRecurrenceTemplateRepository : IRecurrenceTemplateRepository
    {
        private readonly List<RecurrenceTemplate> _items = [];

        public Task<RecurrenceTemplate?> GetByIdAsync(RecurrenceTemplateId id, CancellationToken cancellationToken = default)
            => Task.FromResult(_items.FirstOrDefault(t => t.Id.Equals(id)));

        public Task<IReadOnlyList<RecurrenceTemplate>> ListEnabledAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RecurrenceTemplate>>(_items.Where(t => t.IsEnabled).ToList());

        public Task<IReadOnlyList<RecurrenceTemplate>> ListAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RecurrenceTemplate>>(_items.ToList());


        public Task InsertAsync(RecurrenceTemplate template, CancellationToken cancellationToken = default)
        {
            _items.Add(template);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(RecurrenceTemplate template, CancellationToken cancellationToken = default)
        {
            var idx = _items.FindIndex(t => t.Id.Equals(template.Id));
            if (idx >= 0)
            {
                _items[idx] = template;
            }
            return Task.CompletedTask;
        }
    }
}
