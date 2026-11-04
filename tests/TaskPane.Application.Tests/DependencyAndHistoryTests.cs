using Microsoft.Extensions.Logging.Abstractions;
using TaskPane.Application.Models;
using TaskPane.Application.Services;
using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using TaskPane.Core.Errors;
using TaskPane.Core.Ids;
using TaskPane.Core.Interfaces;
using OverlayPlacement = TaskPane.Core.Entities.OverlayPlacement;

namespace TaskPane.Application.Tests;

public sealed class DependencyAndHistoryTests
{
    [Fact]
    public async Task Add_required_rejects_self_and_cycle()
    {
        var harness = CreateHarness();
        var a = await harness.Tasks.CreateAsync(new CreateTaskRequest { Title = "A" });
        var b = await harness.Tasks.CreateAsync(new CreateTaskRequest { Title = "B" });

        var self = await Assert.ThrowsAsync<BusinessException>(() =>
            harness.Dependencies.AddRequiredAsync(a.Id, a.Id));
        Assert.Equal(ValidationCodes.DependencySelfEdge, self.Code);

        await harness.Dependencies.AddRequiredAsync(a.Id, b.Id);
        var cycle = await Assert.ThrowsAsync<BusinessException>(() =>
            harness.Dependencies.AddRequiredAsync(b.Id, a.Id));
        Assert.Equal(ValidationCodes.DependencyCycle, cycle.Code);
    }

    [Fact]
    public async Task Completing_blocker_unblocks_dependent_immediately()
    {
        var harness = CreateHarness();
        var blocker = await harness.Tasks.CreateAsync(new CreateTaskRequest { Title = "Blocker" });
        var dependent = await harness.Tasks.CreateAsync(new CreateTaskRequest { Title = "Dependent" });
        await harness.Dependencies.AddRequiredAsync(blocker.Id, dependent.Id);

        Assert.True((await harness.Dependencies.GetBlockedStateAsync(dependent.Id)).IsBlocked);

        await harness.Tasks.CompleteAsync(blocker.Id, ParentCompletionChoice.CompleteParentOnly);
        Assert.False((await harness.Dependencies.GetBlockedStateAsync(dependent.Id)).IsBlocked);

        await harness.Tasks.ReopenAsync(blocker.Id);
        Assert.True((await harness.Dependencies.GetBlockedStateAsync(dependent.Id)).IsBlocked);
    }

    [Fact]
    public async Task Soft_delete_preserves_dependency_edges_with_warning()
    {
        var harness = CreateHarness();
        var blocker = await harness.Tasks.CreateAsync(new CreateTaskRequest { Title = "Blocker" });
        var dependent = await harness.Tasks.CreateAsync(new CreateTaskRequest { Title = "Dependent" });
        await harness.Dependencies.AddRequiredAsync(blocker.Id, dependent.Id);

        await harness.Tasks.SoftDeleteAsync(blocker.Id);

        var edges = await harness.DependencyRepo.ListAsync(includeDeleted: false);
        Assert.Single(edges);
        Assert.Equal(blocker.Id, edges[0].PredecessorTaskId);

        var state = await harness.Dependencies.GetBlockedStateAsync(dependent.Id);
        Assert.True(state.IsBlocked);
        Assert.True(state.HasDeletedPredecessorWarning);

        var info = await harness.Dependencies.GetTaskDependencyInfoAsync(dependent.Id);
        Assert.Contains(info.DependsOn, l => l.OtherIsDeleted);
    }

    [Fact]
    public async Task Hide_and_only_blocked_filters_work()
    {
        var harness = CreateHarness();
        var blocker = await harness.Tasks.CreateAsync(new CreateTaskRequest
        {
            Title = "Blocker",
            FocusBucket = FocusBucket.Today
        });
        var dependent = await harness.Tasks.CreateAsync(new CreateTaskRequest
        {
            Title = "Dependent",
            FocusBucket = FocusBucket.Today
        });
        var free = await harness.Tasks.CreateAsync(new CreateTaskRequest
        {
            Title = "Free",
            FocusBucket = FocusBucket.Today
        });
        await harness.Dependencies.AddRequiredAsync(blocker.Id, dependent.Id);

        var onlyBlocked = await harness.Tasks.QueryRootsAsync(new TaskQuery
        {
            FocusBucket = FocusBucket.Today,
            OnlyBlocked = true
        });
        Assert.Contains(onlyBlocked, t => t.Id.Equals(dependent.Id));
        Assert.DoesNotContain(onlyBlocked, t => t.Id.Equals(free.Id));

        var hideBlocked = await harness.Tasks.QueryRootsAsync(new TaskQuery
        {
            FocusBucket = FocusBucket.Today,
            HideBlocked = true
        });
        Assert.DoesNotContain(hideBlocked, t => t.Id.Equals(dependent.Id));
        Assert.Contains(hideBlocked, t => t.Id.Equals(free.Id));
    }

    [Fact]
    public async Task Timer_can_start_on_blocked_task()
    {
        var harness = CreateHarness();
        var blocker = await harness.Tasks.CreateAsync(new CreateTaskRequest { Title = "Blocker" });
        var dependent = await harness.Tasks.CreateAsync(new CreateTaskRequest { Title = "Dependent" });
        await harness.Dependencies.AddRequiredAsync(blocker.Id, dependent.Id);

        Assert.True((await harness.Dependencies.GetBlockedStateAsync(dependent.Id)).IsBlocked);
        var session = await harness.Time.StartOrSwitchAsync(dependent.Id);
        Assert.True(session.IsOpen);
        Assert.Equal(dependent.Id, session.TaskId);
    }

    [Fact]
    public async Task Undo_completion_sleep_move_and_soft_delete()
    {
        var harness = CreateHarness();
        var parent = await harness.Tasks.CreateAsync(new CreateTaskRequest
        {
            Title = "Parent",
            FocusBucket = FocusBucket.Today
        });
        var child = await harness.Tasks.CreateAsync(new CreateTaskRequest
        {
            Title = "Child",
            ParentTaskId = parent.Id
        });
        var other = await harness.Tasks.CreateAsync(new CreateTaskRequest
        {
            Title = "Other",
            FocusBucket = FocusBucket.Next
        });

        await harness.Tasks.CompleteAsync(other.Id, null);
        Assert.True((await harness.Tasks.GetAsync(other.Id))!.IsCompleted);
        await harness.History.UndoLatestAsync();
        Assert.False((await harness.Tasks.GetAsync(other.Id))!.IsCompleted);

        await harness.Tasks.SleepAsync(new SleepTaskRequest
        {
            Id = other.Id,
            Preset = SleepPreset.Indefinitely
        });
        Assert.True((await harness.Tasks.GetAsync(other.Id))!.SleepIndefinitely);
        await harness.History.UndoLatestAsync();
        Assert.False((await harness.Tasks.GetAsync(other.Id))!.SleepIndefinitely);

        await harness.Tasks.MoveAsync(child.Id, other.Id);
        Assert.Equal(other.Id, (await harness.Tasks.GetAsync(child.Id))!.ParentTaskId);
        await harness.History.UndoLatestAsync();
        Assert.Equal(parent.Id, (await harness.Tasks.GetAsync(child.Id))!.ParentTaskId);

        await harness.Tasks.SoftDeleteAsync(parent.Id);
        Assert.True((await harness.Tasks.GetAsync(parent.Id))!.IsDeleted);
        Assert.True((await harness.Tasks.GetAsync(child.Id))!.IsDeleted);
        await harness.History.UndoLatestAsync();
        Assert.False((await harness.Tasks.GetAsync(parent.Id))!.IsDeleted);
        Assert.False((await harness.Tasks.GetAsync(child.Id))!.IsDeleted);
    }

    [Fact]
    public async Task Undo_already_applied_is_rejected()
    {
        var harness = CreateHarness();
        var task = await harness.Tasks.CreateAsync(new CreateTaskRequest { Title = "T" });
        await harness.Tasks.CompleteAsync(task.Id, null);
        var eventId = (await harness.History.PeekUndoAsync())!.Id;
        await harness.History.UndoAsync(eventId);

        var ex = await Assert.ThrowsAsync<BusinessException>(() => harness.History.UndoAsync(eventId));
        Assert.Equal(ValidationCodes.UndoAlreadyApplied, ex.Code);
    }

    [Fact]
    public async Task Recycle_bin_entries_warn_on_deleted_predecessors()
    {
        var harness = CreateHarness();
        var blocker = await harness.Tasks.CreateAsync(new CreateTaskRequest { Title = "Blocker" });
        var dependent = await harness.Tasks.CreateAsync(new CreateTaskRequest { Title = "Dependent" });
        await harness.Dependencies.AddRequiredAsync(blocker.Id, dependent.Id);
        await harness.Tasks.SoftDeleteAsync(blocker.Id);
        await harness.Tasks.SoftDeleteAsync(dependent.Id);

        var entries = await harness.Dependencies.ListRecycleBinEntriesAsync();
        var dependentEntry = Assert.Single(entries, e => e.Task.Id.Equals(dependent.Id));
        Assert.Contains("predecessor", dependentEntry.Warning, StringComparison.OrdinalIgnoreCase);
    }

    private static Harness CreateHarness()
    {
        var tasks = new InMemoryTaskRepository();
        var tags = new InMemoryTagRepository();
        var projects = new InMemoryProjectRepository();
        var deps = new InMemoryDependencyRepository();
        var audit = new InMemoryAuditRepository();
        var uow = new PassthroughUnitOfWork();
        var sessions = new InMemoryTimeSessionRepository();
        var settings = new InMemorySettingsRepository();
        var clock = new FixedClock(DateTimeOffset.Parse("2026-11-04T12:00:00Z"));
        var focus = new FocusService(tasks, tags, deps);
        var time = new TimeTrackingService(
            sessions,
            tasks,
            settings,
            uow,
            clock,
            NullLogger<TimeTrackingService>.Instance);
        var history = new HistoryService(audit, tasks, uow, NullLogger<HistoryService>.Instance);
        var dependencies = new DependencyService(
            deps,
            tasks,
            uow,
            history,
            NullLogger<DependencyService>.Instance);
        var taskService = new TaskService(
            tasks,
            tags,
            projects,
            uow,
            focus,
            time,
            history,
            NullLogger<TaskService>.Instance);
        return new Harness(taskService, dependencies, history, time, deps);
    }

    private sealed record Harness(
        TaskService Tasks,
        DependencyService Dependencies,
        HistoryService History,
        TimeTrackingService Time,
        InMemoryDependencyRepository DependencyRepo);

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

        public Task InsertAsync(TimeSession session, CancellationToken cancellationToken = default)
        {
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
        private readonly List<Tag> _tags = [];
        private readonly Dictionary<TaskId, List<TagId>> _links = [];

        public Task<Tag?> GetByIdAsync(TagId id, CancellationToken cancellationToken = default)
            => Task.FromResult(_tags.FirstOrDefault(t => t.Id.Equals(id)));

        public Task<Tag?> FindByNameAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult(_tags.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<Tag>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Tag>>(_tags.ToList());

        public Task InsertAsync(Tag tag, CancellationToken cancellationToken = default)
        {
            _tags.Add(tag);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Tag tag, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SetTaskTagsAsync(TaskId taskId, IReadOnlyList<TagId> tagIds, CancellationToken cancellationToken = default)
        {
            _links[taskId] = tagIds.ToList();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TagId>> GetTagIdsForTaskAsync(TaskId taskId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TagId>>(_links.TryGetValue(taskId, out var ids) ? ids : []);

        public Task<IReadOnlyDictionary<TaskId, IReadOnlyList<TagId>>> GetTagIdsForTasksAsync(
            IReadOnlyList<TaskId> taskIds,
            CancellationToken cancellationToken = default)
        {
            var map = taskIds.ToDictionary(
                id => id,
                id => (IReadOnlyList<TagId>)(_links.TryGetValue(id, out var ids) ? ids : []));
            return Task.FromResult<IReadOnlyDictionary<TaskId, IReadOnlyList<TagId>>>(map);
        }
    }

    private sealed class InMemoryProjectRepository : IProjectRepository
    {
        private readonly List<Project> _projects = [];

        public Task<Project?> GetByIdAsync(ProjectId id, CancellationToken cancellationToken = default)
            => Task.FromResult(_projects.FirstOrDefault(p => p.Id.Equals(id)));

        public Task<IReadOnlyList<Project>> ListAsync(bool includeArchived = false, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Project>>(
                _projects.Where(p => includeArchived || !p.IsArchived).ToList());

        public Task InsertAsync(Project project, CancellationToken cancellationToken = default)
        {
            _projects.Add(project);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Project project, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class InMemoryDependencyRepository : IDependencyRepository
    {
        public List<DependencyEdge> Items { get; } = [];

        public Task<DependencyEdge?> GetByIdAsync(DependencyId id, CancellationToken cancellationToken = default)
            => Task.FromResult(Items.FirstOrDefault(e => e.Id.Equals(id)));

        public Task<IReadOnlyList<DependencyEdge>> ListAsync(bool includeDeleted = false, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DependencyEdge>>(
                Items.Where(e => includeDeleted || !e.IsDeleted).ToList());

        public Task<IReadOnlyList<DependencyEdge>> ListForTaskAsync(
            TaskId taskId,
            bool includeDeleted = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DependencyEdge>>(
                Items.Where(e =>
                    (includeDeleted || !e.IsDeleted)
                    && (e.PredecessorTaskId.Equals(taskId) || e.SuccessorTaskId.Equals(taskId))).ToList());

        public Task<DependencyEdge?> FindEdgeAsync(
            TaskId predecessorId,
            TaskId successorId,
            DependencyKind kind,
            bool includeDeleted = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Items.FirstOrDefault(e =>
                e.PredecessorTaskId.Equals(predecessorId)
                && e.SuccessorTaskId.Equals(successorId)
                && e.Kind == kind
                && (includeDeleted || !e.IsDeleted)));

        public Task InsertAsync(DependencyEdge edge, CancellationToken cancellationToken = default)
        {
            Items.Add(Clone(edge));
            return Task.CompletedTask;
        }

        public Task UpdateAsync(DependencyEdge edge, CancellationToken cancellationToken = default)
        {
            var idx = Items.FindIndex(e => e.Id.Equals(edge.Id));
            Items[idx] = Clone(edge);
            return Task.CompletedTask;
        }

        public Task SoftDeleteAsync(DependencyId id, DateTimeOffset deletedUtc, CancellationToken cancellationToken = default)
        {
            var edge = Items.First(e => e.Id.Equals(id));
            edge.DeletedUtc = deletedUtc;
            return Task.CompletedTask;
        }

        private static DependencyEdge Clone(DependencyEdge edge) => new()
        {
            Id = edge.Id,
            PredecessorTaskId = edge.PredecessorTaskId,
            SuccessorTaskId = edge.SuccessorTaskId,
            Kind = edge.Kind,
            CreatedUtc = edge.CreatedUtc,
            DeletedUtc = edge.DeletedUtc
        };
    }

    private sealed class InMemoryAuditRepository : IAuditRepository
    {
        public List<AuditEvent> Items { get; } = [];

        public Task InsertAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            Items.Add(Clone(auditEvent));
            return Task.CompletedTask;
        }

        public Task UpdateAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            var idx = Items.FindIndex(e => e.Id.Equals(auditEvent.Id));
            Items[idx] = Clone(auditEvent);
            return Task.CompletedTask;
        }

        public Task<AuditEvent?> GetByIdAsync(AuditEventId id, CancellationToken cancellationToken = default)
        {
            var item = Items.FirstOrDefault(e => e.Id.Equals(id));
            return Task.FromResult(item is null ? null : Clone(item));
        }

        public Task<IReadOnlyList<AuditEvent>> ListRecentAsync(int limit = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AuditEvent>>(
                Items.OrderByDescending(e => e.TimestampUtc).Take(limit).Select(Clone).ToList());

        public Task<IReadOnlyList<AuditEvent>> ListForEntityAsync(
            string entityType,
            string entityId,
            int limit = 50,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AuditEvent>>(
                Items.Where(e => e.EntityType == entityType && e.EntityId == entityId)
                    .OrderByDescending(e => e.TimestampUtc)
                    .Take(limit)
                    .Select(Clone)
                    .ToList());

        public Task<AuditEvent?> GetLatestUndoableAsync(CancellationToken cancellationToken = default)
        {
            var item = Items
                .Where(e => !e.IsUndone && e.Action is AuditAction.Completed or AuditAction.Slept or AuditAction.Moved or AuditAction.SoftDeleted)
                .OrderByDescending(e => e.TimestampUtc)
                .FirstOrDefault();
            return Task.FromResult(item is null ? null : Clone(item));
        }

        private static AuditEvent Clone(AuditEvent e) => new()
        {
            Id = e.Id,
            EntityType = e.EntityType,
            EntityId = e.EntityId,
            Action = e.Action,
            TimestampUtc = e.TimestampUtc,
            BeforeJson = e.BeforeJson,
            AfterJson = e.AfterJson,
            CorrelationId = e.CorrelationId,
            IsUndone = e.IsUndone
        };
    }
}
