using Microsoft.Extensions.Logging.Abstractions;
using TaskPane.Application.Models;
using TaskPane.Application.Services;
using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using TaskPane.Core.Ids;
using TaskPane.Core.Interfaces;
using TaskPane.Core.Recurrence;

namespace TaskPane.Application.Tests;

public sealed class RecurrenceAndReminderTests
{
    [Fact]
    public async Task Completing_november_occurrence_does_not_complete_december()
    {
        var harness = CreateHarness(DateTimeOffset.Parse("2026-11-04T12:00:00Z"));
        var seed = await harness.Tasks.CreateAsync(new CreateTaskRequest
        {
            Title = "Monthly report",
            FocusBucket = FocusBucket.Today
        });

        var template = await harness.Recurrence.AttachAsync(new AttachRecurrenceRequest
        {
            TaskId = seed.Id,
            Rule = new RecurrenceRule
            {
                Frequency = RecurrenceFrequency.Monthly,
                MonthlyMode = MonthlyRecurrenceMode.DayOfMonth,
                DayOfMonth = 4,
                Interval = 1,
                AnchorLocalDate = "2026-11-04"
            },
            TimeZoneId = TimeZoneInfo.Utc.Id,
            ConvertSourceToFirstOccurrence = true
        });

        var nov = await harness.TaskRepo.FindByOccurrenceKeyAsync(template.Id, "2026-11-04");
        Assert.NotNull(nov);
        Assert.False(nov!.IsCompleted);

        await harness.Tasks.CompleteAsync(nov.Id, ParentCompletionChoice.CompleteParentOnly);

        var refreshedNov = await harness.TaskRepo.GetByIdAsync(nov.Id);
        Assert.True(refreshedNov!.IsCompleted);

        var dec = await harness.TaskRepo.FindByOccurrenceKeyAsync(template.Id, "2026-12-04");
        Assert.NotNull(dec);
        Assert.False(dec!.IsCompleted);
        Assert.NotEqual(nov.Id, dec.Id);
    }

    [Fact]
    public async Task Restart_evaluation_twice_creates_no_duplicate_occurrences()
    {
        var harness = CreateHarness(DateTimeOffset.Parse("2026-11-10T12:00:00Z"));
        var seed = await harness.Tasks.CreateAsync(new CreateTaskRequest { Title = "Daily" });
        var template = await harness.Recurrence.AttachAsync(new AttachRecurrenceRequest
        {
            TaskId = seed.Id,
            Rule = new RecurrenceRule
            {
                Frequency = RecurrenceFrequency.Daily,
                Interval = 1,
                AnchorLocalDate = "2026-11-01"
            },
            TimeZoneId = TimeZoneInfo.Utc.Id,
            ConvertSourceToFirstOccurrence = true
        });

        // Seed became Nov 1; evaluate should materialize latest due (Nov 10) once only if no open,
        // but open Nov 1 exists — policy caps catch-up. Complete Nov 1 first.
        await harness.Tasks.CompleteAsync(seed.Id, ParentCompletionChoice.CompleteParentOnly);

        var first = await harness.Recurrence.EvaluateTemplateAsync(template.Id);
        var second = await harness.Recurrence.EvaluateTemplateAsync(template.Id);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first!.Id, second!.Id);

        var all = await harness.TaskRepo.ListByRecurrenceTemplateAsync(template.Id);
        Assert.Equal(all.Count, all.Select(t => t.OccurrenceKey).Distinct().Count());
    }

    [Fact]
    public async Task Edit_this_occurrence_detaches_without_changing_template()
    {
        var harness = CreateHarness(DateTimeOffset.Parse("2026-11-04T12:00:00Z"));
        var seed = await harness.Tasks.CreateAsync(new CreateTaskRequest { Title = "Series" });
        var template = await harness.Recurrence.AttachAsync(new AttachRecurrenceRequest
        {
            TaskId = seed.Id,
            Rule = new RecurrenceRule
            {
                Frequency = RecurrenceFrequency.Weekly,
                Interval = 1,
                Weekdays = [(int)DayOfWeek.Wednesday],
                AnchorLocalDate = "2026-11-04"
            },
            TimeZoneId = TimeZoneInfo.Utc.Id
        });

        await harness.Recurrence.EditOccurrenceAsync(new EditOccurrenceRequest
        {
            TaskId = seed.Id,
            Scope = OccurrenceEditScope.ThisOccurrenceOnly,
            Title = "One-off title"
        });

        var edited = await harness.TaskRepo.GetByIdAsync(seed.Id);
        Assert.Equal("One-off title", edited!.Title);
        Assert.Null(edited.RecurrenceTemplateId);
        Assert.Equal("2026-11-04", edited.OccurrenceKey);

        var still = await harness.Recurrence.GetTemplateAsync(template.Id);
        Assert.True(still!.IsEnabled);
        Assert.Contains("Series", still.ContentSnapshotJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Edit_future_updates_template_snapshot_and_rule()
    {
        var harness = CreateHarness(DateTimeOffset.Parse("2026-11-04T12:00:00Z"));
        var seed = await harness.Tasks.CreateAsync(new CreateTaskRequest { Title = "Old title" });
        var template = await harness.Recurrence.AttachAsync(new AttachRecurrenceRequest
        {
            TaskId = seed.Id,
            Rule = new RecurrenceRule
            {
                Frequency = RecurrenceFrequency.Daily,
                Interval = 1,
                AnchorLocalDate = "2026-11-04"
            },
            TimeZoneId = TimeZoneInfo.Utc.Id
        });

        await harness.Recurrence.EditOccurrenceAsync(new EditOccurrenceRequest
        {
            TaskId = seed.Id,
            Scope = OccurrenceEditScope.ThisAndFuture,
            Title = "New title",
            FutureRule = new RecurrenceRule
            {
                Frequency = RecurrenceFrequency.Daily,
                Interval = 2,
                AnchorLocalDate = "2026-11-04"
            }
        });

        var updated = await harness.Recurrence.GetTemplateAsync(template.Id);
        var rule = harness.Recurrence.ParseRule(updated!);
        Assert.Equal(2, rule!.Interval);
        Assert.Contains("New title", updated!.ContentSnapshotJson, StringComparison.Ordinal);
        Assert.NotNull((await harness.TaskRepo.GetByIdAsync(seed.Id))!.RecurrenceTemplateId);
    }

    [Fact]
    public async Task Reminder_quiet_hours_defer_and_dedup_delivery()
    {
        var harness = CreateHarness(DateTimeOffset.Parse("2026-11-04T23:00:00Z")); // quiet
        var task = await harness.Tasks.CreateAsync(new CreateTaskRequest { Title = "Ping me" });
        var reminder = await harness.Reminders.ScheduleAsync(task.Id, DateTimeOffset.Parse("2026-11-04T22:30:00Z"));

        var deferred = await harness.Reminders.ProcessDueAsync();
        Assert.Contains(NotificationDeliveryStatus.DeferredQuietHours, deferred);
        Assert.Empty(harness.Notifier.Delivered);

        var still = await harness.ReminderRepo.GetByIdAsync(reminder.Id);
        Assert.Null(still!.DeliveredUtc);

        harness.Clock.UtcNow = DateTimeOffset.Parse("2026-11-05T08:00:00Z");
        var delivered = await harness.Reminders.ProcessDueAsync();
        Assert.Contains(NotificationDeliveryStatus.FallbackInApp, delivered);
        Assert.Single(harness.Notifier.Delivered);
        Assert.Equal("Ping me", harness.Notifier.Delivered[0].Title);

        var again = await harness.Reminders.ProcessDueAsync();
        Assert.DoesNotContain(NotificationDeliveryStatus.FallbackInApp, again);
        Assert.Single(harness.Notifier.Delivered);
    }

    [Fact]
    public async Task Reminder_snooze_clears_delivery_and_uses_new_key()
    {
        var harness = CreateHarness(DateTimeOffset.Parse("2026-11-05T10:00:00Z"));
        var task = await harness.Tasks.CreateAsync(new CreateTaskRequest { Title = "Snooze me" });
        var reminder = await harness.Reminders.ScheduleAsync(task.Id, DateTimeOffset.Parse("2026-11-05T09:00:00Z"));
        await harness.Reminders.ProcessDueAsync();

        var beforeKey = (await harness.ReminderRepo.GetByIdAsync(reminder.Id))!.DeliveryKey;
        await harness.Reminders.SnoozeAsync(reminder.Id, TimeSpan.FromMinutes(20));
        var after = await harness.ReminderRepo.GetByIdAsync(reminder.Id);
        Assert.Null(after!.DeliveredUtc);
        Assert.NotEqual(beforeKey, after.DeliveryKey);
        Assert.Equal(harness.Clock.UtcNow.AddMinutes(20), after.DueUtc);
    }

    [Fact]
    public async Task Leap_year_month_end_generation_stable()
    {
        var harness = CreateHarness(DateTimeOffset.Parse("2024-01-31T12:00:00Z"));
        var seed = await harness.Tasks.CreateAsync(new CreateTaskRequest { Title = "Leap" });
        var template = await harness.Recurrence.AttachAsync(new AttachRecurrenceRequest
        {
            TaskId = seed.Id,
            Rule = new RecurrenceRule
            {
                Frequency = RecurrenceFrequency.Monthly,
                MonthlyMode = MonthlyRecurrenceMode.DayOfMonth,
                DayOfMonth = 31,
                Interval = 1,
                AnchorLocalDate = "2024-01-31"
            },
            TimeZoneId = TimeZoneInfo.Utc.Id,
            ConvertSourceToFirstOccurrence = true
        });

        await harness.Tasks.CompleteAsync(seed.Id, ParentCompletionChoice.CompleteParentOnly);
        var feb = await harness.TaskRepo.FindByOccurrenceKeyAsync(template.Id, "2024-02-29");
        Assert.NotNull(feb);
        Assert.False(feb!.IsCompleted);

        harness.Clock.UtcNow = DateTimeOffset.Parse("2024-02-29T12:00:00Z");
        var again = await harness.Recurrence.EvaluateTemplateAsync(template.Id);
        Assert.NotNull(again);
        Assert.Equal(feb.Id, again!.Id);
    }

    private static Harness CreateHarness(DateTimeOffset utcNow)
    {
        var tasks = new InMemoryTaskRepository();
        var templates = new InMemoryRecurrenceTemplateRepository();
        var reminders = new InMemoryReminderRepository();
        var tags = new InMemoryTagRepository();
        var projects = new InMemoryProjectRepository();
        var deps = new InMemoryDependencyRepository();
        var audit = new InMemoryAuditRepository();
        var sessions = new InMemoryTimeSessionRepository();
        var settings = new InMemorySettingsRepository();
        var notifier = new CapturingNotificationPresenter();
        var uow = new PassthroughUnitOfWork();
        var clock = new FixedClock(utcNow);
        var focus = new FocusService(tasks, tags, deps);
        var time = new TimeTrackingService(
            sessions, tasks, settings, uow, clock, NullLogger<TimeTrackingService>.Instance);
        var history = new HistoryService(audit, tasks, deps, tags, uow, NullLogger<HistoryService>.Instance);
        var recurrence = new RecurrenceService(
            templates, tasks, uow, clock, NullLogger<RecurrenceService>.Instance);
        var reminderService = new ReminderService(
            reminders, tasks, settings, notifier, uow, clock, NullLogger<ReminderService>.Instance);
        var taskService = new TaskService(
            tasks, tags, projects, uow, focus, time, history, recurrence, NullLogger<TaskService>.Instance);
        return new Harness(taskService, recurrence, reminderService, tasks, reminders, notifier, clock);
    }

    private sealed record Harness(
        TaskService Tasks,
        RecurrenceService Recurrence,
        ReminderService Reminders,
        InMemoryTaskRepository TaskRepo,
        InMemoryReminderRepository ReminderRepo,
        CapturingNotificationPresenter Notifier,
        FixedClock Clock);

    private sealed class CapturingNotificationPresenter : INotificationPresenter
    {
        public List<ReminderNotification> Delivered { get; } = [];

        public Task<NotificationDeliveryStatus> ShowAsync(
            ReminderNotification notification,
            CancellationToken cancellationToken = default)
        {
            Delivered.Add(notification);
            return Task.FromResult(NotificationDeliveryStatus.FallbackInApp);
        }
    }

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
            return Task.FromResult(query.Select(t => t.ManualSortKey).DefaultIfEmpty(0).Max() + 1000);
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
            _items.Add(Clone(template));
            return Task.CompletedTask;
        }

        public Task UpdateAsync(RecurrenceTemplate template, CancellationToken cancellationToken = default)
        {
            var idx = _items.FindIndex(t => t.Id.Equals(template.Id));
            _items[idx] = Clone(template);
            return Task.CompletedTask;
        }

        private static RecurrenceTemplate Clone(RecurrenceTemplate t) => new()
        {
            Id = t.Id,
            SourceTaskId = t.SourceTaskId,
            RuleJson = t.RuleJson,
            ContentSnapshotJson = t.ContentSnapshotJson,
            TimeZoneId = t.TimeZoneId,
            NextEvaluationUtc = t.NextEvaluationUtc,
            IsEnabled = t.IsEnabled,
            LastSkippedOccurrenceKey = t.LastSkippedOccurrenceKey,
            CreatedUtc = t.CreatedUtc,
            UpdatedUtc = t.UpdatedUtc
        };
    }

    private sealed class InMemoryReminderRepository : IReminderRepository
    {
        public List<Reminder> Items { get; } = [];

        public Task<Reminder?> GetByIdAsync(ReminderId id, CancellationToken cancellationToken = default)
            => Task.FromResult(Items.FirstOrDefault(r => r.Id.Equals(id)) is { } r ? Clone(r) : null);

        public Task<Reminder?> GetByDeliveryKeyAsync(string deliveryKey, CancellationToken cancellationToken = default)
            => Task.FromResult(Items.FirstOrDefault(r => r.DeliveryKey == deliveryKey) is { } r ? Clone(r) : null);

        public Task<IReadOnlyList<Reminder>> ListForTaskAsync(TaskId taskId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Reminder>>(Items.Where(r => r.TaskId.Equals(taskId)).Select(Clone).ToList());

        public Task<IReadOnlyList<Reminder>> ListDueAsync(DateTimeOffset utcNow, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Reminder>>(
                Items.Where(r => r.DismissedUtc is null && r.DeliveredUtc is null && r.DueUtc <= utcNow)
                    .Select(Clone)
                    .ToList());

        public Task InsertAsync(Reminder reminder, CancellationToken cancellationToken = default)
        {
            Items.Add(Clone(reminder));
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Reminder reminder, CancellationToken cancellationToken = default)
        {
            var idx = Items.FindIndex(r => r.Id.Equals(reminder.Id));
            Items[idx] = Clone(reminder);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(ReminderId id, CancellationToken cancellationToken = default)
        {
            Items.RemoveAll(r => r.Id.Equals(id));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Reminder>> ListAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Reminder>>(Items.Select(Clone).ToList());

        private static Reminder Clone(Reminder r) => new()
        {
            Id = r.Id,
            TaskId = r.TaskId,
            DueUtc = r.DueUtc,
            DeliveredUtc = r.DeliveredUtc,
            DismissedUtc = r.DismissedUtc,
            SnoozedUntilUtc = r.SnoozedUntilUtc,
            Kind = r.Kind,
            DeliveryKey = r.DeliveryKey,
            CreatedUtc = r.CreatedUtc
        };
    }

    private sealed class InMemoryTimeSessionRepository : ITimeSessionRepository
    {
        public Task<TimeSession?> GetByIdAsync(SessionId id, CancellationToken cancellationToken = default)
            => Task.FromResult<TimeSession?>(null);

        public Task<TimeSession?> GetOpenSessionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<TimeSession?>(null);

        public Task<IReadOnlyList<TimeSession>> ListForTaskAsync(
            TaskId taskId, bool includeDiscarded = false, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TimeSession>>([]);

        public Task<IReadOnlyList<TimeSession>> ListForTasksAsync(
            IReadOnlyList<TaskId> taskIds, bool includeDiscarded = false, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TimeSession>>([]);

        public Task<IReadOnlyList<TimeSession>> ListInRangeAsync(
            DateTimeOffset rangeStartUtc, DateTimeOffset rangeEndUtc, bool includeDiscarded = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TimeSession>>([]);

        public Task<IReadOnlyList<TimeSession>> ListAllAsync(
            bool includeDiscarded = true,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TimeSession>>([]);

        public Task InsertAsync(TimeSession session, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateAsync(TimeSession session, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteAsync(SessionId id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class InMemoryTagRepository : ITagRepository
    {
        public Task<Tag?> GetByIdAsync(TagId id, CancellationToken cancellationToken = default) => Task.FromResult<Tag?>(null);
        public Task<Tag?> FindByNameAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult<Tag?>(null);
        public Task<IReadOnlyList<Tag>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Tag>>([]);
        public Task InsertAsync(Tag tag, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateAsync(Tag tag, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetTaskTagsAsync(TaskId taskId, IReadOnlyList<TagId> tagIds, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<TagId>> GetTagIdsForTaskAsync(TaskId taskId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TagId>>([]);
        public Task<IReadOnlyDictionary<TaskId, IReadOnlyList<TagId>>> GetTagIdsForTasksAsync(IReadOnlyList<TaskId> taskIds, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<TaskId, IReadOnlyList<TagId>>>(taskIds.ToDictionary(id => id, _ => (IReadOnlyList<TagId>)[]));

        public Task<IReadOnlyList<(TaskId TaskId, TagId TagId)>> ListAllTaskTagsAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<(TaskId TaskId, TagId TagId)>>([]);
    }

    private sealed class InMemoryProjectRepository : IProjectRepository
    {
        public Task<Project?> GetByIdAsync(ProjectId id, CancellationToken cancellationToken = default) => Task.FromResult<Project?>(null);
        public Task<IReadOnlyList<Project>> ListAsync(bool includeArchived = false, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Project>>([]);
        public Task InsertAsync(Project project, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateAsync(Project project, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class InMemoryDependencyRepository : IDependencyRepository
    {
        public Task<DependencyEdge?> GetByIdAsync(DependencyId id, CancellationToken cancellationToken = default) => Task.FromResult<DependencyEdge?>(null);
        public Task<IReadOnlyList<DependencyEdge>> ListAsync(bool includeDeleted = false, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DependencyEdge>>([]);
        public Task<IReadOnlyList<DependencyEdge>> ListForTaskAsync(TaskId taskId, bool includeDeleted = false, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DependencyEdge>>([]);
        public Task<DependencyEdge?> FindEdgeAsync(TaskId predecessorId, TaskId successorId, DependencyKind kind, bool includeDeleted = false, CancellationToken cancellationToken = default) => Task.FromResult<DependencyEdge?>(null);
        public Task InsertAsync(DependencyEdge edge, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateAsync(DependencyEdge edge, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SoftDeleteAsync(DependencyId id, DateTimeOffset deletedUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class InMemoryAuditRepository : IAuditRepository
    {
        public Task InsertAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<AuditEvent?> GetByIdAsync(AuditEventId id, CancellationToken cancellationToken = default) => Task.FromResult<AuditEvent?>(null);
        public Task<IReadOnlyList<AuditEvent>> ListRecentAsync(int limit = 100, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AuditEvent>>([]);
        public Task<IReadOnlyList<AuditEvent>> ListForEntityAsync(string entityType, string entityId, int limit = 50, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AuditEvent>>([]);
        public Task<AuditEvent?> GetLatestUndoableAsync(CancellationToken cancellationToken = default) => Task.FromResult<AuditEvent?>(null);
        public Task<IReadOnlyList<AuditEvent>> ListAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AuditEvent>>([]);
    }

}
