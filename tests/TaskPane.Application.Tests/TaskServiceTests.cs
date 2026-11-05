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

public sealed class TaskServiceTests
{
    [Fact]
    public async Task Create_requires_title()
    {
        var sut = CreateSut(out _);
        await Assert.ThrowsAsync<BusinessException>(() => sut.CreateAsync(new CreateTaskRequest { Title = " " }));
    }

    [Fact]
    public async Task Create_defaults_to_inbox_normal()
    {
        var sut = CreateSut(out var repo);
        var task = await sut.CreateAsync(new CreateTaskRequest { Title = "Capture me" });
        Assert.Equal(FocusBucket.Inbox, task.FocusBucket);
        Assert.Equal(TaskPriority.Normal, task.Priority);
        Assert.Single(repo.Items);
    }

    [Fact]
    public async Task Rejects_grandchild()
    {
        var sut = CreateSut(out _);
        var parent = await sut.CreateAsync(new CreateTaskRequest { Title = "Parent" });
        var child = await sut.CreateAsync(new CreateTaskRequest { Title = "Child", ParentTaskId = parent.Id });

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            sut.CreateAsync(new CreateTaskRequest { Title = "Grand", ParentTaskId = child.Id }));
        Assert.Equal(ValidationCodes.ParentIsChild, ex.Code);
    }

    [Fact]
    public async Task Create_subtask_placeholder_then_rename_keeps_one_level()
    {
        var sut = CreateSut(out _);
        var parent = await sut.CreateAsync(new CreateTaskRequest
        {
            Title = "Parent",
            FocusBucket = FocusBucket.Today
        });
        var child = await sut.CreateAsync(new CreateTaskRequest
        {
            Title = TaskPane.Core.Validation.SubtaskUiPolicy.NewSubtaskPlaceholderTitle,
            ParentTaskId = parent.Id
        });

        Assert.Equal(parent.Id, child.ParentTaskId);
        Assert.Equal(FocusBucket.Today, child.FocusBucket);

        var renamed = await sut.UpdateAsync(new UpdateTaskRequest
        {
            Id = child.Id,
            Title = "Prepare migration script",
            Priority = child.Priority,
            FocusBucket = child.FocusBucket
        });

        Assert.Equal("Prepare migration script", renamed.Title);
        Assert.Equal(parent.Id, renamed.ParentTaskId);

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            sut.CreateAsync(new CreateTaskRequest
            {
                Title = "Too deep",
                ParentTaskId = renamed.Id
            }));
        Assert.Equal(ValidationCodes.ParentIsChild, ex.Code);
    }

    [Fact]
    public async Task Soft_delete_parent_cascades_and_restore_works()
    {
        var sut = CreateSut(out var repo);
        var parent = await sut.CreateAsync(new CreateTaskRequest { Title = "Parent" });
        await sut.CreateAsync(new CreateTaskRequest { Title = "Child", ParentTaskId = parent.Id });

        await sut.SoftDeleteAsync(parent.Id);
        Assert.Equal(2, repo.Items.Count(t => t.IsDeleted));
        var batch = repo.Items.First(t => t.Id.Equals(parent.Id)).DeletionBatchId!;
        await sut.RestoreBatchAsync(batch.Value);
        Assert.All(repo.Items, t => Assert.False(t.IsDeleted));
    }

    [Fact]
    public async Task Complete_parent_with_open_children_requires_choice()
    {
        var sut = CreateSut(out _);
        var parent = await sut.CreateAsync(new CreateTaskRequest { Title = "Parent" });
        await sut.CreateAsync(new CreateTaskRequest { Title = "Child", ParentTaskId = parent.Id });

        var blocked = await sut.CompleteAsync(parent.Id, null);
        Assert.False(blocked.Completed);
        Assert.Equal(1, blocked.IncompleteChildCount);

        var done = await sut.CompleteAsync(parent.Id, ParentCompletionChoice.CompleteParentAndChildren);
        Assert.True(done.Completed);
    }

    [Fact]
    public async Task Manual_order_and_focus_are_independent()
    {
        var sut = CreateSut(out _);
        var a = await sut.CreateAsync(new CreateTaskRequest { Title = "A", FocusBucket = FocusBucket.Today, Priority = TaskPriority.Low });
        var b = await sut.CreateAsync(new CreateTaskRequest { Title = "B", FocusBucket = FocusBucket.Today, Priority = TaskPriority.High });
        await sut.ReorderAsync(b.Id, a.ManualSortKey - 10);

        var list = await sut.ListVisibleRootsAsync(FocusBucket.Today);
        Assert.Equal(b.Id, list[0].Id);
        Assert.Equal(TaskPriority.High, list[0].Priority);
    }

    [Fact]
    public async Task Undated_high_priority_ranks_ahead_of_dated_low_when_sorting_by_priority()
    {
        var sut = CreateSut(out _);
        var datedLow = await sut.CreateAsync(new CreateTaskRequest
        {
            Title = "Dated low",
            FocusBucket = FocusBucket.Today,
            Priority = TaskPriority.Low,
            DeadlineLocalDate = "2026-11-01"
        });
        var undatedHigh = await sut.CreateAsync(new CreateTaskRequest
        {
            Title = "Undated high",
            FocusBucket = FocusBucket.Today,
            Priority = TaskPriority.High
        });

        var list = await sut.QueryRootsAsync(new TaskQuery
        {
            FocusBucket = FocusBucket.Today,
            SortMode = TaskSortMode.Priority,
            IncludeSleeping = true
        });

        Assert.Equal(undatedHigh.Id, list[0].Id);
        Assert.Equal(datedLow.Id, list[1].Id);
        Assert.Null(list[0].DeadlineLocalDate);
        Assert.Equal(TaskPriority.High, list[0].Priority);
    }

    [Fact]
    public async Task Sleep_and_wake_hide_and_restore_without_changing_deadline_or_priority()
    {
        var sut = CreateSut(out var repo);
        var task = await sut.CreateAsync(new CreateTaskRequest
        {
            Title = "Sleepy",
            FocusBucket = FocusBucket.Today,
            Priority = TaskPriority.High,
            DeadlineLocalDate = "2026-12-01"
        });

        await sut.SleepAsync(new SleepTaskRequest { Id = task.Id, Preset = SleepPreset.Indefinitely });
        var sleeping = repo.Items.Single(t => t.Id.Equals(task.Id));
        Assert.True(sleeping.SleepIndefinitely);
        Assert.Equal(TaskPriority.High, sleeping.Priority);
        Assert.Equal("2026-12-01", sleeping.DeadlineLocalDate);
        Assert.Empty(await sut.ListVisibleRootsAsync(FocusBucket.Today));

        await sut.WakeAsync(task.Id);
        Assert.Single(await sut.ListVisibleRootsAsync(FocusBucket.Today));
        Assert.False(repo.Items.Single(t => t.Id.Equals(task.Id)).SleepIndefinitely);
    }

    [Fact]
    public async Task Sleeping_parent_hides_children_in_overlay_query()
    {
        var sut = CreateSut(out _);
        var parent = await sut.CreateAsync(new CreateTaskRequest { Title = "Parent", FocusBucket = FocusBucket.Today });
        var child = await sut.CreateAsync(new CreateTaskRequest { Title = "Child", ParentTaskId = parent.Id });

        await sut.SleepAsync(new SleepTaskRequest { Id = parent.Id, Preset = SleepPreset.OneHour });
        var visibleChildren = await sut.QueryChildrenAsync(parent.Id, new TaskQuery
        {
            OverlayVisibility = true,
            FocusBucket = FocusBucket.Today
        });
        Assert.Empty(visibleChildren);
        Assert.DoesNotContain(await sut.ListVisibleRootsAsync(FocusBucket.Today), t => t.Id.Equals(parent.Id));
        Assert.Contains(await sut.ListSleepingRootsAsync(), t => t.Id.Equals(parent.Id));
        _ = child;
    }

    [Fact]
    public async Task Custom_sleep_hides_until_expiry_then_auto_wakes_without_clearing_fields()
    {
        var sut = CreateSut(out var repo);
        var task = await sut.CreateAsync(new CreateTaskRequest
        {
            Title = "Custom nap",
            FocusBucket = FocusBucket.Today,
            Priority = TaskPriority.High,
            Notes = "keep me",
            DeadlineLocalDate = "2026-12-15"
        });

        var until = DateTimeOffset.UtcNow.AddMinutes(45);
        await sut.SleepAsync(new SleepTaskRequest
        {
            Id = task.Id,
            Preset = SleepPreset.Custom,
            CustomUntilUtc = until
        });

        var sleeping = repo.Items.Single(t => t.Id.Equals(task.Id));
        Assert.False(sleeping.SleepIndefinitely);
        Assert.Equal(until, sleeping.SleepUntilUtc);
        Assert.Equal("keep me", sleeping.Notes);
        Assert.Equal(TaskPriority.High, sleeping.Priority);
        Assert.Equal("2026-12-15", sleeping.DeadlineLocalDate);
        Assert.Empty(await sut.ListVisibleRootsAsync(FocusBucket.Today));
        Assert.Contains(await sut.ListSleepingRootsAsync(), t => t.Id.Equals(task.Id));

        // Simulate clock past wake: evaluator hides no longer; stored fields stay until explicit wake.
        sleeping.SleepUntilUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        Assert.Contains(await sut.ListVisibleRootsAsync(FocusBucket.Today), t => t.Id.Equals(task.Id));
        Assert.DoesNotContain(await sut.ListSleepingRootsAsync(), t => t.Id.Equals(task.Id));
        Assert.Equal("keep me", repo.Items.Single(t => t.Id.Equals(task.Id)).Notes);
        Assert.Equal("2026-12-15", repo.Items.Single(t => t.Id.Equals(task.Id)).DeadlineLocalDate);
    }

    [Fact]
    public async Task Sleeping_subtask_hides_only_itself_parent_and_sibling_stay_visible()
    {
        var sut = CreateSut(out _);
        var parent = await sut.CreateAsync(new CreateTaskRequest { Title = "Parent", FocusBucket = FocusBucket.Today });
        var childA = await sut.CreateAsync(new CreateTaskRequest { Title = "A", ParentTaskId = parent.Id });
        var childB = await sut.CreateAsync(new CreateTaskRequest { Title = "B", ParentTaskId = parent.Id });

        await sut.SleepAsync(new SleepTaskRequest { Id = childA.Id, Preset = SleepPreset.TomorrowMorning });

        Assert.Contains(await sut.ListVisibleRootsAsync(FocusBucket.Today), t => t.Id.Equals(parent.Id));
        var visibleChildren = await sut.QueryChildrenAsync(parent.Id, new TaskQuery
        {
            OverlayVisibility = true,
            FocusBucket = FocusBucket.Today
        });
        Assert.DoesNotContain(visibleChildren, t => t.Id.Equals(childA.Id));
        Assert.Contains(visibleChildren, t => t.Id.Equals(childB.Id));
    }

    [Fact]
    public async Task Wake_immediately_restores_visibility()
    {
        var sut = CreateSut(out _);
        var task = await sut.CreateAsync(new CreateTaskRequest { Title = "Wake me", FocusBucket = FocusBucket.Today });
        await sut.SleepAsync(new SleepTaskRequest { Id = task.Id, Preset = SleepPreset.NextWeek });
        Assert.Empty(await sut.ListVisibleRootsAsync(FocusBucket.Today));

        await sut.WakeAsync(task.Id);
        Assert.Contains(await sut.ListVisibleRootsAsync(FocusBucket.Today), t => t.Id.Equals(task.Id));
        Assert.DoesNotContain(await sut.ListSleepingRootsAsync(), t => t.Id.Equals(task.Id));
    }

    [Fact]
    public async Task Search_filters_by_title_and_notes()
    {
        var sut = CreateSut(out _);
        await sut.CreateAsync(new CreateTaskRequest { Title = "Alpha", FocusBucket = FocusBucket.Inbox });
        var withNotes = await sut.CreateAsync(new CreateTaskRequest
        {
            Title = "Beta",
            Notes = "secret keyword here",
            FocusBucket = FocusBucket.Inbox
        });

        var byNotes = await sut.QueryRootsAsync(new TaskQuery
        {
            FocusBucket = FocusBucket.Inbox,
            SearchText = "keyword",
            IncludeSleeping = true
        });
        Assert.Single(byNotes);
        Assert.Equal(withNotes.Id, byNotes[0].Id);
    }

    [Fact]
    public async Task Waiting_is_independent_of_sleep()
    {
        var sut = CreateSut(out var repo);
        var task = await sut.CreateAsync(new CreateTaskRequest { Title = "Wait", FocusBucket = FocusBucket.Next });
        await sut.SetWaitingAsync(task.Id, "Vendor reply", DateTimeOffset.UtcNow.AddDays(2));
        await sut.SleepAsync(new SleepTaskRequest { Id = task.Id, Preset = SleepPreset.Indefinitely });

        var stored = repo.Items.Single(t => t.Id.Equals(task.Id));
        Assert.Equal("Vendor reply", stored.WaitingFor);
        Assert.True(stored.SleepIndefinitely);

        var onlyWaiting = await sut.QueryRootsAsync(new TaskQuery
        {
            OnlyWaiting = true,
            IncludeSleeping = true
        });
        Assert.Single(onlyWaiting);
    }

    [Fact]
    public async Task Move_rejects_parent_with_children_under_another_parent()
    {
        var sut = CreateSut(out _);
        var a = await sut.CreateAsync(new CreateTaskRequest { Title = "A" });
        await sut.CreateAsync(new CreateTaskRequest { Title = "A1", ParentTaskId = a.Id });
        var b = await sut.CreateAsync(new CreateTaskRequest { Title = "B" });

        var ex = await Assert.ThrowsAsync<BusinessException>(() => sut.MoveAsync(a.Id, b.Id));
        Assert.Equal(ValidationCodes.ParentHasChildren, ex.Code);
    }

    [Fact]
    public async Task Move_allows_subtask_between_roots_and_promote()
    {
        var sut = CreateSut(out _);
        var a = await sut.CreateAsync(new CreateTaskRequest { Title = "A", FocusBucket = FocusBucket.Today });
        var b = await sut.CreateAsync(new CreateTaskRequest { Title = "B", FocusBucket = FocusBucket.Next });
        var child = await sut.CreateAsync(new CreateTaskRequest { Title = "Child", ParentTaskId = a.Id });

        await sut.MoveAsync(child.Id, b.Id);
        var underB = await sut.ListChildrenAsync(b.Id);
        Assert.Contains(underB, t => t.Id.Equals(child.Id));
        Assert.Equal(FocusBucket.Next, underB.Single(t => t.Id.Equals(child.Id)).FocusBucket);

        await sut.MoveAsync(child.Id, null);
        Assert.Empty(await sut.ListChildrenAsync(b.Id));
        Assert.Contains(await sut.ListVisibleRootsAsync(FocusBucket.Next), t => t.Id.Equals(child.Id));
    }

    private static TaskService CreateSut(out InMemoryTaskRepository repo)
    {
        repo = new InMemoryTaskRepository();
        var tags = new InMemoryTagRepository();
        var projects = new InMemoryProjectRepository();
        var deps = new InMemoryDependencyRepository();
        var audit = new InMemoryAuditRepository();
        var focus = new FocusService(repo, tags, deps);
        var uow = new PassthroughUnitOfWork();
        var sessions = new InMemoryTimeSessionRepository();
        var settings = new InMemorySettingsRepository();
        var clock = new FixedClock(DateTimeOffset.Parse("2026-11-04T12:00:00Z"));
        var time = new TimeTrackingService(
            sessions,
            repo,
            settings,
            uow,
            clock,
            NullLogger<TimeTrackingService>.Instance);
        var history = new HistoryService(audit, repo, uow, NullLogger<HistoryService>.Instance);
        var recurrence = new RecurrenceService(
            new InMemoryRecurrenceTemplateRepository(),
            repo,
            uow,
            clock,
            NullLogger<RecurrenceService>.Instance);
        return new TaskService(
            repo,
            tags,
            projects,
            uow,
            focus,
            time,
            history,
            recurrence,
            NullLogger<TaskService>.Instance);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
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
            if (session.IsOpen && Items.Any(s => s.IsOpen))
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

    private sealed class PassthroughUnitOfWork : IUnitOfWork
    {
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
            => action(cancellationToken);
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
        public Task<IReadOnlyList<(TaskId TaskId, TagId TagId)>> ListAllTaskTagsAsync(
            CancellationToken cancellationToken = default)
        {
            var list = new List<(TaskId, TagId)>();
            foreach (var kv in _links)
            {
                foreach (var tagId in kv.Value)
                {
                    list.Add((kv.Key, tagId));
                }
            }
            return Task.FromResult<IReadOnlyList<(TaskId, TagId)>>(list);
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
            Items.Add(edge);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(DependencyEdge edge, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SoftDeleteAsync(DependencyId id, DateTimeOffset deletedUtc, CancellationToken cancellationToken = default)
        {
            var edge = Items.First(e => e.Id.Equals(id));
            edge.DeletedUtc = deletedUtc;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryAuditRepository : IAuditRepository
    {
        public List<AuditEvent> Items { get; } = [];

        public Task InsertAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            Items.Add(auditEvent);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<AuditEvent?> GetByIdAsync(AuditEventId id, CancellationToken cancellationToken = default)
            => Task.FromResult(Items.FirstOrDefault(e => e.Id.Equals(id)));

        public Task<IReadOnlyList<AuditEvent>> ListRecentAsync(int limit = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AuditEvent>>(Items.Take(limit).ToList());

        public Task<IReadOnlyList<AuditEvent>> ListForEntityAsync(
            string entityType,
            string entityId,
            int limit = 50,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AuditEvent>>(
                Items.Where(e => e.EntityType == entityType && e.EntityId == entityId).Take(limit).ToList());

        public Task<AuditEvent?> GetLatestUndoableAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Items.LastOrDefault(e => !e.IsUndone));

        public Task<IReadOnlyList<AuditEvent>> ListAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AuditEvent>>(Items.ToList());
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
