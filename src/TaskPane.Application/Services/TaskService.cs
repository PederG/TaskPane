using Microsoft.Extensions.Logging;
using TaskPane.Application.Models;
using TaskPane.Application.Validation;
using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using TaskPane.Core.Errors;
using TaskPane.Core.Ids;
using TaskPane.Core.Interfaces;
using TaskPane.Core.Sleep;
using TaskPane.Core.Validation;

namespace TaskPane.Application.Services;

public sealed class TaskService
{
    private readonly ITaskRepository _tasks;
    private readonly ITagRepository _tags;
    private readonly IProjectRepository _projects;
    private readonly IUnitOfWork _unitOfWork;
    private readonly FocusService _focus;
    private readonly TimeTrackingService _timeTracking;
    private readonly HistoryService _history;
    private readonly RecurrenceService _recurrence;
    private readonly ILogger<TaskService> _logger;

    public TaskService(
        ITaskRepository tasks,
        ITagRepository tags,
        IProjectRepository projects,
        IUnitOfWork unitOfWork,
        FocusService focus,
        TimeTrackingService timeTracking,
        HistoryService history,
        RecurrenceService recurrence,
        ILogger<TaskService> logger)
    {
        _tasks = tasks;
        _tags = tags;
        _projects = projects;
        _unitOfWork = unitOfWork;
        _focus = focus;
        _timeTracking = timeTracking;
        _history = history;
        _recurrence = recurrence;
        _logger = logger;
    }

    public async Task<TaskItem> CreateAsync(
        CreateTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        var title = TaskTitle.NormalizeOrThrow(request.Title);
        TaskItem? parent = null;

        if (request.ParentTaskId is { } parentId)
        {
            parent = await _tasks.GetByIdAsync(parentId, cancellationToken).ConfigureAwait(false);
            TaskHierarchyValidator.EnsureExists(parent, parentId);
            TaskHierarchyValidator.EnsureCanBecomeParent(parent!);
        }

        DeadlineValidator.Validate(request.DeadlineLocalDate, request.DeadlineLocalTime);
        if (request.ProjectId is { } projectId)
        {
            await EnsureProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
        }

        var now = DateTimeOffset.UtcNow;
        var sortKey = await _tasks
            .GetNextSortKeyAsync(request.ParentTaskId, request.ParentTaskId is null ? request.FocusBucket : null, cancellationToken)
            .ConfigureAwait(false);

        var task = new TaskItem
        {
            Id = TaskId.New(),
            ParentTaskId = request.ParentTaskId,
            Title = title,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            Priority = request.Priority,
            FocusBucket = parent?.FocusBucket ?? request.FocusBucket,
            Status = TaskLifecycleStatus.Open,
            ManualSortKey = sortKey,
            DeadlineLocalDate = DeadlineValidator.NormalizeDate(request.DeadlineLocalDate),
            DeadlineLocalTime = DeadlineValidator.NormalizeTime(request.DeadlineLocalTime),
            DeadlineTimeZoneId = string.IsNullOrWhiteSpace(request.DeadlineTimeZoneId)
                ? null
                : request.DeadlineTimeZoneId.Trim(),
            ProjectId = request.ProjectId,
            CreatedUtc = now,
            UpdatedUtc = now,
            Revision = 1
        };

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await _tasks.InsertAsync(task, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Created task {TaskId} (parent={HasParent})", task.Id, task.ParentTaskId is not null);
        return task;
    }

    public async Task<TaskItem> UpdateAsync(
        UpdateTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        var title = TaskTitle.NormalizeOrThrow(request.Title);
        var task = await RequireAsync(request.Id, cancellationToken).ConfigureAwait(false);
        DeadlineValidator.Validate(request.DeadlineLocalDate, request.DeadlineLocalTime);

        if (request.ProjectId is { } projectId)
        {
            await EnsureProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
        }

        if (request.TagIds is not null)
        {
            foreach (var tagId in request.TagIds)
            {
                var tag = await _tags.GetByIdAsync(tagId, cancellationToken).ConfigureAwait(false);
                if (tag is null)
                {
                    throw new BusinessException(ValidationCodes.TagNotFound, "One or more tags were not found.");
                }
            }
        }

        task.Title = title;
        task.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        task.Priority = request.Priority;
        task.FocusBucket = request.FocusBucket;
        task.DeadlineLocalDate = DeadlineValidator.NormalizeDate(request.DeadlineLocalDate);
        task.DeadlineLocalTime = DeadlineValidator.NormalizeTime(request.DeadlineLocalTime);
        task.DeadlineTimeZoneId = string.IsNullOrWhiteSpace(request.DeadlineTimeZoneId)
            ? null
            : request.DeadlineTimeZoneId.Trim();
        task.WaitingFor = string.IsNullOrWhiteSpace(request.WaitingFor) ? null : request.WaitingFor.Trim();
        task.WaitingFollowUpUtc = string.IsNullOrWhiteSpace(request.WaitingFor) ? null : request.WaitingFollowUpUtc;
        task.ProjectId = request.ProjectId;
        task.UpdatedUtc = DateTimeOffset.UtcNow;
        task.Revision++;

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await _tasks.UpdateAsync(task, ct).ConfigureAwait(false);
            if (request.TagIds is not null)
            {
                await _tags.SetTaskTagsAsync(task.Id, request.TagIds, ct).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);

        return task;
    }

    public async Task SleepAsync(SleepTaskRequest request, CancellationToken cancellationToken = default)
    {
        var task = await RequireAsync(request.Id, cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;

        if (request.Preset == SleepPreset.Custom && request.CustomUntilUtc is null)
        {
            throw new BusinessException(ValidationCodes.InvalidSleep, "Custom sleep requires an until timestamp.");
        }

        if (request.Preset == SleepPreset.Custom && request.CustomUntilUtc <= now)
        {
            throw new BusinessException(ValidationCodes.InvalidSleep, "Custom sleep must be in the future.");
        }

        var before = HistoryService.TaskSnapshot.From(task);
        var until = SleepEvaluator.ResolveWakeUtc(request.Preset, now, TimeZoneInfo.Local, request.CustomUntilUtc);
        task.SleepIndefinitely = request.Preset == SleepPreset.Indefinitely;
        task.SleepUntilUtc = task.SleepIndefinitely ? null : until;
        task.UpdatedUtc = now;
        task.Revision++;

        // Stop the slept task's open timer, and when sleeping a parent also stop any
        // descendant that holds the (single) open session. Child sleep stays independent —
        // siblings / unrelated tasks keep running. CloseSession preserves elapsed time.
        // Wake / schedule expiry only restore visibility; they never restart a timer.
        var stopTimerIds = new List<TaskId> { task.Id };
        if (task.IsRoot)
        {
            var children = await _tasks.ListChildrenAsync(task.Id, includeDeleted: false, cancellationToken)
                .ConfigureAwait(false);
            stopTimerIds.AddRange(children.Select(c => c.Id));
        }

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            foreach (var stopId in stopTimerIds)
            {
                await _timeTracking.EndActiveForTaskAsync(stopId, SessionEndReason.Slept, ct)
                    .ConfigureAwait(false);
            }

            await _tasks.UpdateAsync(task, ct).ConfigureAwait(false);
            await _history.RecordSleptAsync(before, task, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Slept task {TaskId} preset={Preset}", task.Id, request.Preset);
    }

    public async Task WakeAsync(TaskId id, CancellationToken cancellationToken = default)
    {
        var task = await RequireAsync(id, cancellationToken).ConfigureAwait(false);
        task.SleepIndefinitely = false;
        task.SleepUntilUtc = null;
        task.UpdatedUtc = DateTimeOffset.UtcNow;
        task.Revision++;

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await _tasks.UpdateAsync(task, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetWaitingAsync(
        TaskId id,
        string? waitingFor,
        DateTimeOffset? followUpUtc,
        CancellationToken cancellationToken = default)
    {
        var task = await RequireAsync(id, cancellationToken).ConfigureAwait(false);
        task.WaitingFor = string.IsNullOrWhiteSpace(waitingFor) ? null : waitingFor.Trim();
        task.WaitingFollowUpUtc = task.WaitingFor is null ? null : followUpUtc;
        task.UpdatedUtc = DateTimeOffset.UtcNow;
        task.Revision++;

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await _tasks.UpdateAsync(task, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetDeadlineAsync(
        TaskId id,
        string? localDate,
        string? localTime,
        string? timeZoneId,
        CancellationToken cancellationToken = default)
    {
        DeadlineValidator.Validate(localDate, localTime);
        var task = await RequireAsync(id, cancellationToken).ConfigureAwait(false);
        task.DeadlineLocalDate = DeadlineValidator.NormalizeDate(localDate);
        task.DeadlineLocalTime = DeadlineValidator.NormalizeTime(localTime);
        task.DeadlineTimeZoneId = string.IsNullOrWhiteSpace(timeZoneId) ? null : timeZoneId.Trim();
        task.UpdatedUtc = DateTimeOffset.UtcNow;
        task.Revision++;

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await _tasks.UpdateAsync(task, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CompleteTaskResult> CompleteAsync(
        TaskId id,
        ParentCompletionChoice? choice,
        CancellationToken cancellationToken = default)
    {
        var task = await RequireAsync(id, cancellationToken).ConfigureAwait(false);
        if (task.IsCompleted)
        {
            return new CompleteTaskResult
            {
                Completed = true,
                AppliedChoice = ParentCompletionChoice.CompleteParentOnly,
                Task = task
            };
        }

        var children = await _tasks.ListChildrenAsync(id, includeDeleted: false, cancellationToken).ConfigureAwait(false);
        var incomplete = children.Where(c => !c.IsCompleted).ToList();

        if (incomplete.Count > 0)
        {
            if (choice is null or ParentCompletionChoice.Cancel)
            {
                return new CompleteTaskResult
                {
                    Completed = false,
                    AppliedChoice = ParentCompletionChoice.Cancel,
                    IncompleteChildCount = incomplete.Count,
                    Task = task
                };
            }
        }

        var applied = choice ?? ParentCompletionChoice.CompleteParentOnly;
        var now = DateTimeOffset.UtcNow;
        var completedChildren = new List<HistoryService.TaskSnapshot>();

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            if (applied == ParentCompletionChoice.CompleteParentAndChildren)
            {
                foreach (var child in incomplete)
                {
                    await _timeTracking.EndActiveForTaskAsync(child.Id, SessionEndReason.Completed, ct)
                        .ConfigureAwait(false);
                    child.Status = TaskLifecycleStatus.Completed;
                    child.CompletedUtc = now;
                    child.UpdatedUtc = now;
                    child.Revision++;
                    await _tasks.UpdateAsync(child, ct).ConfigureAwait(false);
                    completedChildren.Add(HistoryService.TaskSnapshot.From(child));
                }
            }

            await _timeTracking.EndActiveForTaskAsync(task.Id, SessionEndReason.Completed, ct)
                .ConfigureAwait(false);
            task.Status = TaskLifecycleStatus.Completed;
            task.CompletedUtc = now;
            task.UpdatedUtc = now;
            task.Revision++;
            await _tasks.UpdateAsync(task, ct).ConfigureAwait(false);
            await _history.RecordCompletedAsync(task, completedChildren, applied, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        // Completing an occurrence generates the next independent instance (does not complete futures).
        await _recurrence.EnsureNextAfterCompletionAsync(task, cancellationToken).ConfigureAwait(false);

        return new CompleteTaskResult
        {
            Completed = true,
            AppliedChoice = applied,
            Task = task,
            IncompleteChildCount = incomplete.Count
        };
    }

    public async Task<TaskItem> ReopenAsync(TaskId id, CancellationToken cancellationToken = default)
    {
        var task = await RequireAsync(id, cancellationToken).ConfigureAwait(false);
        task.Status = TaskLifecycleStatus.Open;
        task.CompletedUtc = null;
        task.UpdatedUtc = DateTimeOffset.UtcNow;
        task.Revision++;

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await _tasks.UpdateAsync(task, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        return task;
    }

    public async Task SoftDeleteAsync(TaskId id, CancellationToken cancellationToken = default)
    {
        var task = await RequireAsync(id, cancellationToken).ConfigureAwait(false);
        var batchId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var toDelete = new List<TaskItem> { task };

        if (task.IsRoot)
        {
            var children = await _tasks.ListChildrenAsync(task.Id, includeDeleted: false, cancellationToken)
                .ConfigureAwait(false);
            toDelete.AddRange(children);
        }

        var ids = toDelete.Select(t => t.Id).ToList();
        var snapshots = toDelete.Select(HistoryService.TaskSnapshot.From).ToList();

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            foreach (var softId in ids)
            {
                await _timeTracking.EndActiveForTaskAsync(softId, SessionEndReason.Deleted, ct)
                    .ConfigureAwait(false);
            }

            // Soft-delete never removes dependency edges; links remain restorable with a warning.
            await _tasks.SoftDeleteBatchAsync(ids, batchId, now, ct).ConfigureAwait(false);
            await _history.RecordSoftDeletedAsync(snapshots, batchId, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Soft-deleted {Count} task(s) in batch {BatchId}", ids.Count, batchId);
    }

    public async Task RestoreBatchAsync(Guid deletionBatchId, CancellationToken cancellationToken = default)
    {
        var items = await _tasks.ListByDeletionBatchAsync(deletionBatchId, cancellationToken).ConfigureAwait(false);
        if (items.Count == 0)
        {
            throw new BusinessException(ValidationCodes.NotDeleted, "No deleted tasks found for that batch.");
        }

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await _tasks.RestoreBatchAsync(deletionBatchId, ct).ConfigureAwait(false);

            // Safe handling of missing parents: promote orphans to roots rather than failing.
            foreach (var item in items)
            {
                var restored = await _tasks.GetByIdAsync(item.Id, ct).ConfigureAwait(false);
                if (restored?.ParentTaskId is not { } parentId)
                {
                    continue;
                }

                var parent = await _tasks.GetByIdAsync(parentId, ct).ConfigureAwait(false);
                if (parent is null)
                {
                    restored.ParentTaskId = null;
                    restored.UpdatedUtc = DateTimeOffset.UtcNow;
                    restored.Revision++;
                    await _tasks.UpdateAsync(restored, ct).ConfigureAwait(false);
                    _logger.LogInformation(
                        "Restored task {TaskId} promoted to root because parent was missing",
                        restored.Id);
                }
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task ReorderAsync(
        TaskId id,
        double newSortKey,
        CancellationToken cancellationToken = default)
    {
        if (double.IsNaN(newSortKey) || double.IsInfinity(newSortKey))
        {
            throw new BusinessException(ValidationCodes.InvalidOrder, "Sort key must be a finite number.");
        }

        var task = await RequireAsync(id, cancellationToken).ConfigureAwait(false);
        task.ManualSortKey = newSortKey;
        task.UpdatedUtc = DateTimeOffset.UtcNow;
        task.Revision++;

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await _tasks.UpdateAsync(task, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Places <paramref name="id"/> between two neighbors by average sort key (or append/prepend).
    /// </summary>
    public async Task ReorderBetweenAsync(
        TaskId id,
        TaskId? beforeId,
        TaskId? afterId,
        CancellationToken cancellationToken = default)
    {
        var task = await RequireAsync(id, cancellationToken).ConfigureAwait(false);
        double? beforeKey = null;
        double? afterKey = null;

        if (beforeId is { } b)
        {
            var before = await RequireAsync(b, cancellationToken).ConfigureAwait(false);
            EnsureSameList(task, before);
            beforeKey = before.ManualSortKey;
        }

        if (afterId is { } a)
        {
            var after = await RequireAsync(a, cancellationToken).ConfigureAwait(false);
            EnsureSameList(task, after);
            afterKey = after.ManualSortKey;
        }

        double newKey;
        if (beforeKey is { } bk && afterKey is { } ak)
        {
            newKey = (bk + ak) / 2.0;
        }
        else if (beforeKey is { } onlyBefore)
        {
            newKey = onlyBefore + 1000;
        }
        else if (afterKey is { } onlyAfter)
        {
            newKey = onlyAfter - 1000;
        }
        else
        {
            newKey = await _tasks
                .GetNextSortKeyAsync(task.ParentTaskId, task.IsRoot ? task.FocusBucket : null, cancellationToken)
                .ConfigureAwait(false);
        }

        await ReorderAsync(id, newKey, cancellationToken).ConfigureAwait(false);
    }

    public async Task MoveAsync(
        TaskId id,
        TaskId? newParentId,
        CancellationToken cancellationToken = default)
    {
        var task = await RequireAsync(id, cancellationToken).ConfigureAwait(false);
        var before = HistoryService.TaskSnapshot.From(task);
        TaskItem? newParent = null;
        if (newParentId is { } pid)
        {
            newParent = await RequireAsync(pid, cancellationToken).ConfigureAwait(false);
        }

        var children = await _tasks.ListChildrenAsync(task.Id, includeDeleted: false, cancellationToken)
            .ConfigureAwait(false);
        TaskHierarchyValidator.EnsureCanAssignParent(task, newParent, children);

        task.ParentTaskId = newParentId;
        if (newParent is not null)
        {
            task.FocusBucket = newParent.FocusBucket;
        }

        task.ManualSortKey = await _tasks
            .GetNextSortKeyAsync(newParentId, newParentId is null ? task.FocusBucket : null, cancellationToken)
            .ConfigureAwait(false);
        task.UpdatedUtc = DateTimeOffset.UtcNow;
        task.Revision++;

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await _tasks.UpdateAsync(task, ct).ConfigureAwait(false);
            await _history.RecordMovedAsync(before, task, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetFocusAsync(
        TaskId id,
        FocusBucket focus,
        CancellationToken cancellationToken = default)
    {
        var task = await RequireAsync(id, cancellationToken).ConfigureAwait(false);
        task.FocusBucket = focus;
        task.UpdatedUtc = DateTimeOffset.UtcNow;
        task.Revision++;

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await _tasks.UpdateAsync(task, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<TaskItem>> ListVisibleRootsAsync(
        FocusBucket? focus,
        CancellationToken cancellationToken = default)
        => _focus.QueryRootsAsync(new TaskQuery
        {
            FocusBucket = focus,
            OverlayVisibility = true,
            SortMode = TaskSortMode.Manual
        }, cancellationToken: cancellationToken);

    public Task<IReadOnlyList<TaskItem>> QueryRootsAsync(
        TaskQuery query,
        CancellationToken cancellationToken = default)
        => _focus.QueryRootsAsync(query, cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<TaskItem>> ListChildrenAsync(
        TaskId parentId,
        CancellationToken cancellationToken = default)
    {
        var children = await _tasks.ListChildrenAsync(parentId, includeDeleted: false, cancellationToken)
            .ConfigureAwait(false);
        return children
            .OrderBy(t => t.ManualSortKey)
            .ThenBy(t => t.CreatedUtc)
            .ToList();
    }

    public Task<IReadOnlyList<TaskItem>> QueryChildrenAsync(
        TaskId parentId,
        TaskQuery query,
        CancellationToken cancellationToken = default)
        => _focus.QueryChildrenAsync(parentId, query, cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<TaskItem>> ListDeletedAsync(CancellationToken cancellationToken = default)
    {
        var all = await _tasks.ListAsync(includeDeleted: true, cancellationToken).ConfigureAwait(false);
        return all.Where(t => t.IsDeleted).OrderByDescending(t => t.DeletedUtc).ToList();
    }

    public Task<IReadOnlyList<TaskItem>> ListSleepingRootsAsync(CancellationToken cancellationToken = default)
        => _focus.ListSleepingRootsAsync(cancellationToken: cancellationToken);

    public async Task<SubtaskProgress> GetProgressAsync(
        TaskId parentId,
        CancellationToken cancellationToken = default)
    {
        var children = await _tasks.ListChildrenAsync(parentId, includeDeleted: false, cancellationToken)
            .ConfigureAwait(false);
        var total = children.Count;
        var completed = children.Count(c => c.IsCompleted);
        return new SubtaskProgress(parentId, completed, total);
    }

    public Task<TaskItem?> GetAsync(TaskId id, CancellationToken cancellationToken = default)
        => _tasks.GetByIdAsync(id, cancellationToken);

    private async Task EnsureProjectAsync(ProjectId projectId, CancellationToken cancellationToken)
    {
        var project = await _projects.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (project is null || project.IsArchived)
        {
            throw new BusinessException(ValidationCodes.ProjectNotFound, "Project was not found.");
        }
    }

    private static void EnsureSameList(TaskItem moving, TaskItem neighbor)
    {
        if (!Nullable.Equals(moving.ParentTaskId, neighbor.ParentTaskId))
        {
            throw new BusinessException(
                ValidationCodes.InvalidOrder,
                "Reorder neighbors must share the same parent.");
        }
    }

    private async Task<TaskItem> RequireAsync(TaskId id, CancellationToken cancellationToken)
    {
        var task = await _tasks.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        TaskHierarchyValidator.EnsureExists(task, id);
        return task!;
    }
}
