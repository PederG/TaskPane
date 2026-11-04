using Microsoft.Extensions.Logging;
using TaskPane.Application.Models;
using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using TaskPane.Core.Errors;
using TaskPane.Core.Ids;
using TaskPane.Core.Interfaces;
using TaskPane.Core.Validation;

namespace TaskPane.Application.Services;

public sealed class TaskService
{
    private readonly ITaskRepository _tasks;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<TaskService> _logger;

    public TaskService(
        ITaskRepository tasks,
        IUnitOfWork unitOfWork,
        ILogger<TaskService> logger)
    {
        _tasks = tasks;
        _unitOfWork = unitOfWork;
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

        task.Title = title;
        task.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        task.Priority = request.Priority;
        task.FocusBucket = request.FocusBucket;
        task.UpdatedUtc = DateTimeOffset.UtcNow;
        task.Revision++;

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await _tasks.UpdateAsync(task, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        return task;
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

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            if (applied == ParentCompletionChoice.CompleteParentAndChildren)
            {
                foreach (var child in incomplete)
                {
                    child.Status = TaskLifecycleStatus.Completed;
                    child.CompletedUtc = now;
                    child.UpdatedUtc = now;
                    child.Revision++;
                    await _tasks.UpdateAsync(child, ct).ConfigureAwait(false);
                }
            }

            task.Status = TaskLifecycleStatus.Completed;
            task.CompletedUtc = now;
            task.UpdatedUtc = now;
            task.Revision++;
            await _tasks.UpdateAsync(task, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

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
        var ids = new List<TaskId> { task.Id };

        if (task.IsRoot)
        {
            var children = await _tasks.ListChildrenAsync(task.Id, includeDeleted: false, cancellationToken)
                .ConfigureAwait(false);
            ids.AddRange(children.Select(c => c.Id));
        }

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await _tasks.SoftDeleteBatchAsync(ids, batchId, now, ct).ConfigureAwait(false);
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

    public async Task MoveAsync(
        TaskId id,
        TaskId? newParentId,
        CancellationToken cancellationToken = default)
    {
        var task = await RequireAsync(id, cancellationToken).ConfigureAwait(false);
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

    public async Task<IReadOnlyList<TaskItem>> ListVisibleRootsAsync(
        FocusBucket? focus,
        CancellationToken cancellationToken = default)
    {
        var all = await _tasks.ListAsync(includeDeleted: false, cancellationToken).ConfigureAwait(false);
        return all
            .Where(t => t.IsRoot && !t.IsCompleted)
            .Where(t => focus is null || t.FocusBucket == focus)
            .OrderBy(t => t.ManualSortKey)
            .ThenBy(t => t.CreatedUtc)
            .ToList();
    }

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

    public async Task<IReadOnlyList<TaskItem>> ListDeletedAsync(CancellationToken cancellationToken = default)
    {
        var all = await _tasks.ListAsync(includeDeleted: true, cancellationToken).ConfigureAwait(false);
        return all.Where(t => t.IsDeleted).OrderByDescending(t => t.DeletedUtc).ToList();
    }

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

    private async Task<TaskItem> RequireAsync(TaskId id, CancellationToken cancellationToken)
    {
        var task = await _tasks.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        TaskHierarchyValidator.EnsureExists(task, id);
        return task!;
    }
}
