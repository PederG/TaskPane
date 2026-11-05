using System.Text.Json;
using Microsoft.Extensions.Logging;
using TaskPane.Application.Models;
using TaskPane.Core.Dependencies;
using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using TaskPane.Core.Errors;
using TaskPane.Core.Ids;
using TaskPane.Core.Interfaces;

namespace TaskPane.Application.Services;

public sealed class HistoryService
{
    public const string EntityTask = "Task";
    public const string EntityDependency = "Dependency";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly IAuditRepository _audit;
    private readonly ITaskRepository _tasks;
    private readonly IDependencyRepository _dependencies;
    private readonly ITagRepository _tags;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<HistoryService> _logger;

    public HistoryService(
        IAuditRepository audit,
        ITaskRepository tasks,
        IDependencyRepository dependencies,
        ITagRepository tags,
        IUnitOfWork unitOfWork,
        ILogger<HistoryService> logger)
    {
        _audit = audit;
        _tasks = tasks;
        _dependencies = dependencies;
        _tags = tags;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public static bool IsUndoableAction(AuditAction action) => action is
        AuditAction.Updated
        or AuditAction.Completed
        or AuditAction.Slept
        or AuditAction.Moved
        or AuditAction.SoftDeleted
        or AuditAction.DependencyAdded
        or AuditAction.DependencyRemoved;

    public Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
        => _audit.InsertAsync(auditEvent, cancellationToken);

    public async Task RecordUpdatedAsync(
        TaskSnapshot before,
        TaskSnapshot after,
        CancellationToken cancellationToken = default)
    {
        if (TaskSnapshot.ContentEquals(before, after))
        {
            return;
        }

        await _audit.InsertAsync(new AuditEvent
        {
            Id = AuditEventId.New(),
            EntityType = EntityTask,
            EntityId = after.Id,
            Action = AuditAction.Updated,
            TimestampUtc = DateTimeOffset.UtcNow,
            BeforeJson = JsonSerializer.Serialize(before, JsonOptions),
            AfterJson = JsonSerializer.Serialize(after, JsonOptions),
            CorrelationId = Guid.NewGuid()
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordCompletedAsync(
        TaskItem task,
        IReadOnlyList<TaskSnapshot> completedChildren,
        ParentCompletionChoice choice,
        CancellationToken cancellationToken = default)
    {
        var payload = new CompletePayload
        {
            Choice = choice,
            Task = TaskSnapshot.From(task),
            Children = completedChildren.ToList()
        };
        await _audit.InsertAsync(new AuditEvent
        {
            Id = AuditEventId.New(),
            EntityType = EntityTask,
            EntityId = task.Id.ToString(),
            Action = AuditAction.Completed,
            TimestampUtc = DateTimeOffset.UtcNow,
            BeforeJson = JsonSerializer.Serialize(new { status = TaskLifecycleStatus.Open }, JsonOptions),
            AfterJson = JsonSerializer.Serialize(payload, JsonOptions),
            CorrelationId = Guid.NewGuid()
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordSleptAsync(
        TaskSnapshot before,
        TaskItem after,
        CancellationToken cancellationToken = default)
    {
        await _audit.InsertAsync(new AuditEvent
        {
            Id = AuditEventId.New(),
            EntityType = EntityTask,
            EntityId = after.Id.ToString(),
            Action = AuditAction.Slept,
            TimestampUtc = DateTimeOffset.UtcNow,
            BeforeJson = JsonSerializer.Serialize(before, JsonOptions),
            AfterJson = JsonSerializer.Serialize(TaskSnapshot.From(after), JsonOptions),
            CorrelationId = Guid.NewGuid()
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordMovedAsync(
        TaskSnapshot before,
        TaskItem after,
        CancellationToken cancellationToken = default)
    {
        await _audit.InsertAsync(new AuditEvent
        {
            Id = AuditEventId.New(),
            EntityType = EntityTask,
            EntityId = after.Id.ToString(),
            Action = AuditAction.Moved,
            TimestampUtc = DateTimeOffset.UtcNow,
            BeforeJson = JsonSerializer.Serialize(before, JsonOptions),
            AfterJson = JsonSerializer.Serialize(TaskSnapshot.From(after), JsonOptions),
            CorrelationId = Guid.NewGuid()
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordSoftDeletedAsync(
        IReadOnlyList<TaskSnapshot> deleted,
        Guid deletionBatchId,
        CancellationToken cancellationToken = default)
    {
        var primary = deleted[0];
        await _audit.InsertAsync(new AuditEvent
        {
            Id = AuditEventId.New(),
            EntityType = EntityTask,
            EntityId = primary.Id,
            Action = AuditAction.SoftDeleted,
            TimestampUtc = DateTimeOffset.UtcNow,
            BeforeJson = JsonSerializer.Serialize(deleted, JsonOptions),
            AfterJson = JsonSerializer.Serialize(new SoftDeletePayload
            {
                DeletionBatchId = deletionBatchId,
                TaskIds = deleted.Select(d => d.Id).ToList()
            }, JsonOptions),
            CorrelationId = deletionBatchId
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task RecordDependencyAddedAsync(DependencyEdge edge, CancellationToken cancellationToken = default)
        => _audit.InsertAsync(new AuditEvent
        {
            Id = AuditEventId.New(),
            EntityType = EntityDependency,
            EntityId = edge.Id.ToString(),
            Action = AuditAction.DependencyAdded,
            TimestampUtc = DateTimeOffset.UtcNow,
            AfterJson = JsonSerializer.Serialize(DependencySnapshot.From(edge), JsonOptions),
            CorrelationId = Guid.NewGuid()
        }, cancellationToken);

    public Task RecordDependencyRemovedAsync(DependencyEdge edge, CancellationToken cancellationToken = default)
        => _audit.InsertAsync(new AuditEvent
        {
            Id = AuditEventId.New(),
            EntityType = EntityDependency,
            EntityId = edge.Id.ToString(),
            Action = AuditAction.DependencyRemoved,
            TimestampUtc = DateTimeOffset.UtcNow,
            BeforeJson = JsonSerializer.Serialize(DependencySnapshot.From(edge), JsonOptions),
            CorrelationId = Guid.NewGuid()
        }, cancellationToken);

    public Task<IReadOnlyList<AuditEvent>> ListRecentAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
        => _audit.ListRecentAsync(limit, cancellationToken);

    public Task<AuditEvent?> PeekUndoAsync(CancellationToken cancellationToken = default)
        => _audit.GetLatestUndoableAsync(cancellationToken);

    public async Task<UndoEvaluation> EvaluateUndoAsync(
        AuditEventId id,
        CancellationToken cancellationToken = default)
    {
        var audit = await _audit.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (audit is null)
        {
            return UndoEvaluation.Deny(id, AuditAction.Updated, "History entry was not found.");
        }

        return await EvaluateUndoCoreAsync(audit, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AuditEvent> UndoLatestAsync(CancellationToken cancellationToken = default)
    {
        var latest = await _audit.GetLatestUndoableAsync(cancellationToken).ConfigureAwait(false);
        if (latest is null)
        {
            throw new BusinessException(ValidationCodes.AuditNotFound, "Nothing to undo.");
        }

        return await UndoAsync(latest.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AuditEvent> UndoAsync(AuditEventId id, CancellationToken cancellationToken = default)
    {
        var audit = await _audit.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (audit is null)
        {
            throw new BusinessException(ValidationCodes.AuditNotFound, "History entry was not found.");
        }

        var evaluation = await EvaluateUndoCoreAsync(audit, cancellationToken).ConfigureAwait(false);
        if (!evaluation.CanUndo)
        {
            var code = audit.IsUndone
                ? ValidationCodes.UndoAlreadyApplied
                : !IsUndoableAction(audit.Action)
                    ? ValidationCodes.UndoNotSupported
                    : ValidationCodes.UndoConflict;
            throw new BusinessException(code, evaluation.Reason);
        }

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            // Re-load inside the transaction so a concurrent undo cannot double-apply.
            var fresh = await _audit.GetByIdAsync(id, ct).ConfigureAwait(false)
                        ?? throw new BusinessException(ValidationCodes.AuditNotFound, "History entry was not found.");
            if (fresh.IsUndone)
            {
                throw new BusinessException(ValidationCodes.UndoAlreadyApplied, "That change was already undone.");
            }

            var recheck = await EvaluateUndoCoreAsync(fresh, ct).ConfigureAwait(false);
            if (!recheck.CanUndo)
            {
                throw new BusinessException(ValidationCodes.UndoConflict, recheck.Reason);
            }

            switch (fresh.Action)
            {
                case AuditAction.Updated:
                    await UndoUpdatedAsync(fresh, ct).ConfigureAwait(false);
                    break;
                case AuditAction.Completed:
                    await UndoCompletedAsync(fresh, ct).ConfigureAwait(false);
                    break;
                case AuditAction.Slept:
                    await UndoSleptAsync(fresh, ct).ConfigureAwait(false);
                    break;
                case AuditAction.Moved:
                    await UndoMovedAsync(fresh, ct).ConfigureAwait(false);
                    break;
                case AuditAction.SoftDeleted:
                    await UndoSoftDeletedAsync(fresh, ct).ConfigureAwait(false);
                    break;
                case AuditAction.DependencyAdded:
                    await UndoDependencyAddedAsync(fresh, ct).ConfigureAwait(false);
                    break;
                case AuditAction.DependencyRemoved:
                    await UndoDependencyRemovedAsync(fresh, ct).ConfigureAwait(false);
                    break;
                default:
                    throw new BusinessException(
                        ValidationCodes.UndoNotSupported,
                        $"Undo is not supported for {fresh.Action}.");
            }

            fresh.IsUndone = true;
            await _audit.UpdateAsync(fresh, ct).ConfigureAwait(false);
            audit = fresh;
        }, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Undid audit {AuditId} action={Action}", audit.Id, audit.Action);
        return audit;
    }

    private async Task<UndoEvaluation> EvaluateUndoCoreAsync(
        AuditEvent audit,
        CancellationToken cancellationToken)
    {
        if (audit.IsUndone)
        {
            return UndoEvaluation.Deny(audit.Id, audit.Action, "That change was already undone.");
        }

        if (!IsUndoableAction(audit.Action))
        {
            return UndoEvaluation.Deny(
                audit.Id,
                audit.Action,
                $"Undo is not supported for {audit.Action}.");
        }

        try
        {
            switch (audit.Action)
            {
                case AuditAction.Updated:
                    await EnsureUpdatedUndoableAsync(audit, cancellationToken).ConfigureAwait(false);
                    break;
                case AuditAction.Completed:
                    await EnsureCompletedUndoableAsync(audit, cancellationToken).ConfigureAwait(false);
                    break;
                case AuditAction.Slept:
                case AuditAction.Moved:
                    await EnsureTaskAfterMatchesAsync(audit, cancellationToken).ConfigureAwait(false);
                    break;
                case AuditAction.SoftDeleted:
                    await EnsureSoftDeleteUndoableAsync(audit, cancellationToken).ConfigureAwait(false);
                    break;
                case AuditAction.DependencyAdded:
                    await EnsureDependencyAddedUndoableAsync(audit, cancellationToken).ConfigureAwait(false);
                    break;
                case AuditAction.DependencyRemoved:
                    await EnsureDependencyRemovedUndoableAsync(audit, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
        catch (BusinessException ex) when (
            ex.Code is ValidationCodes.UndoConflict
                or ValidationCodes.UndoNotSupported
                or ValidationCodes.TaskNotFound
                or ValidationCodes.DependencyNotFound)
        {
            return UndoEvaluation.Deny(audit.Id, audit.Action, ex.Message);
        }

        return UndoEvaluation.Allow(audit.Id, audit.Action);
    }

    private async Task EnsureUpdatedUndoableAsync(AuditEvent audit, CancellationToken cancellationToken)
    {
        var after = DeserializeSnapshot(audit.AfterJson, "Corrupt update history.");
        var task = await RequireTaskAsync(TaskId.Parse(after.Id), cancellationToken).ConfigureAwait(false);
        if (task.IsDeleted)
        {
            throw new BusinessException(
                ValidationCodes.UndoConflict,
                "Task is in the recycle bin; restore it before undoing field edits.");
        }

        var tags = await _tags.GetTagIdsForTaskAsync(task.Id, cancellationToken).ConfigureAwait(false);
        var current = TaskSnapshot.From(task, tags);
        if (!TaskSnapshot.EditableEquals(current, after))
        {
            throw new BusinessException(
                ValidationCodes.UndoConflict,
                "Task was edited after this change; undo would overwrite newer edits.");
        }
    }

    private async Task EnsureCompletedUndoableAsync(AuditEvent audit, CancellationToken cancellationToken)
    {
        var payload = DeserializeComplete(audit.AfterJson);
        var task = await RequireTaskAsync(TaskId.Parse(payload.Task.Id), cancellationToken).ConfigureAwait(false);
        if (!task.IsCompleted)
        {
            throw new BusinessException(
                ValidationCodes.UndoConflict,
                "Task is no longer completed; undo would overwrite a newer state.");
        }
    }

    private async Task EnsureTaskAfterMatchesAsync(AuditEvent audit, CancellationToken cancellationToken)
    {
        var after = DeserializeSnapshot(audit.AfterJson, "Corrupt history snapshot.");
        var task = await RequireTaskAsync(TaskId.Parse(after.Id), cancellationToken).ConfigureAwait(false);
        if (audit.Action == AuditAction.Slept)
        {
            if (task.SleepIndefinitely != after.SleepIndefinitely || task.SleepUntilUtc != after.SleepUntilUtc)
            {
                throw new BusinessException(
                    ValidationCodes.UndoConflict,
                    "Sleep state changed after this entry; undo would overwrite newer edits.");
            }

            return;
        }

        // Moved: parent / focus / sort must still match the recorded after-state.
        var currentParent = task.ParentTaskId?.ToString();
        if (!string.Equals(currentParent, after.ParentTaskId, StringComparison.Ordinal)
            || task.FocusBucket != after.FocusBucket
            || !task.ManualSortKey.Equals(after.ManualSortKey))
        {
            throw new BusinessException(
                ValidationCodes.UndoConflict,
                "Task was moved or reordered after this entry; undo would overwrite newer edits.");
        }
    }

    private async Task EnsureSoftDeleteUndoableAsync(AuditEvent audit, CancellationToken cancellationToken)
    {
        var payload = DeserializeSoftDelete(audit.AfterJson);
        var anyStillDeleted = false;
        foreach (var id in payload.TaskIds)
        {
            var task = await _tasks.GetByIdAsync(TaskId.Parse(id), cancellationToken).ConfigureAwait(false);
            if (task is null)
            {
                throw new BusinessException(
                    ValidationCodes.UndoConflict,
                    "A deleted task is missing; cannot undo soft delete.");
            }

            if (task.IsDeleted && task.DeletionBatchId == payload.DeletionBatchId)
            {
                anyStillDeleted = true;
            }
            else if (!task.IsDeleted)
            {
                throw new BusinessException(
                    ValidationCodes.UndoConflict,
                    "Task was already restored; undo would overwrite a newer state.");
            }
        }

        if (!anyStillDeleted)
        {
            throw new BusinessException(
                ValidationCodes.UndoConflict,
                "Nothing remains deleted from that batch.");
        }
    }

    private async Task EnsureDependencyAddedUndoableAsync(AuditEvent audit, CancellationToken cancellationToken)
    {
        var snap = DeserializeDependency(audit.AfterJson, "Corrupt dependency-add history.");
        var edge = await _dependencies.GetByIdAsync(DependencyId.Parse(snap.Id), cancellationToken)
            .ConfigureAwait(false);
        if (edge is null || edge.IsDeleted)
        {
            throw new BusinessException(
                ValidationCodes.UndoConflict,
                "Dependency was already removed; undo would overwrite a newer state.");
        }
    }

    private async Task EnsureDependencyRemovedUndoableAsync(AuditEvent audit, CancellationToken cancellationToken)
    {
        var snap = DeserializeDependency(audit.BeforeJson, "Corrupt dependency-remove history.");
        var edge = await _dependencies.GetByIdAsync(DependencyId.Parse(snap.Id), cancellationToken)
            .ConfigureAwait(false);
        if (edge is null)
        {
            throw new BusinessException(
                ValidationCodes.UndoConflict,
                "Dependency edge is missing; cannot undo removal.");
        }

        if (!edge.IsDeleted)
        {
            throw new BusinessException(
                ValidationCodes.UndoConflict,
                "Dependency is already active; undo would overwrite a newer state.");
        }

        var predecessor = await _tasks.GetByIdAsync(TaskId.Parse(snap.PredecessorTaskId), cancellationToken)
            .ConfigureAwait(false);
        var successor = await _tasks.GetByIdAsync(TaskId.Parse(snap.SuccessorTaskId), cancellationToken)
            .ConfigureAwait(false);
        if (predecessor is null || successor is null)
        {
            throw new BusinessException(
                ValidationCodes.UndoConflict,
                "One or both tasks for that dependency are missing.");
        }

        if (edge.Kind == DependencyKind.Required)
        {
            var required = (await _dependencies.ListAsync(includeDeleted: false, cancellationToken)
                    .ConfigureAwait(false))
                .Where(e => e.Kind == DependencyKind.Required)
                .ToList();
            if (DependencyGraph.WouldCreateCycle(
                    required,
                    edge.PredecessorTaskId,
                    edge.SuccessorTaskId))
            {
                throw new BusinessException(
                    ValidationCodes.UndoConflict,
                    "Restoring that dependency would create a cycle.");
            }
        }
    }

    private async Task UndoUpdatedAsync(AuditEvent audit, CancellationToken cancellationToken)
    {
        var before = DeserializeSnapshot(audit.BeforeJson, "Corrupt update history.");
        var task = await RequireTaskAsync(TaskId.Parse(before.Id), cancellationToken).ConfigureAwait(false);
        ApplyEditableFields(task, before);
        task.UpdatedUtc = DateTimeOffset.UtcNow;
        task.Revision++;
        await _tasks.UpdateAsync(task, cancellationToken).ConfigureAwait(false);
        await _tags.SetTaskTagsAsync(
                task.Id,
                before.TagIds.Select(TagId.Parse).ToList(),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task UndoCompletedAsync(AuditEvent audit, CancellationToken cancellationToken)
    {
        var payload = DeserializeComplete(audit.AfterJson);
        await ReopenSnapshotAsync(payload.Task, cancellationToken).ConfigureAwait(false);
        foreach (var child in payload.Children)
        {
            await ReopenSnapshotAsync(child, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task UndoSleptAsync(AuditEvent audit, CancellationToken cancellationToken)
    {
        var before = DeserializeSnapshot(audit.BeforeJson, "Corrupt sleep history.");
        var task = await RequireTaskAsync(TaskId.Parse(before.Id), cancellationToken).ConfigureAwait(false);
        task.SleepIndefinitely = before.SleepIndefinitely;
        task.SleepUntilUtc = before.SleepUntilUtc;
        task.UpdatedUtc = DateTimeOffset.UtcNow;
        task.Revision++;
        await _tasks.UpdateAsync(task, cancellationToken).ConfigureAwait(false);
    }

    private async Task UndoMovedAsync(AuditEvent audit, CancellationToken cancellationToken)
    {
        var before = DeserializeSnapshot(audit.BeforeJson, "Corrupt move history.");
        var task = await RequireTaskAsync(TaskId.Parse(before.Id), cancellationToken).ConfigureAwait(false);

        TaskId? parentId = string.IsNullOrWhiteSpace(before.ParentTaskId)
            ? null
            : TaskId.Parse(before.ParentTaskId);

        if (parentId is { } pid)
        {
            var parent = await _tasks.GetByIdAsync(pid, cancellationToken).ConfigureAwait(false);
            if (parent is null || parent.IsDeleted)
            {
                parentId = null;
            }
        }

        task.ParentTaskId = parentId;
        task.FocusBucket = before.FocusBucket;
        task.ManualSortKey = before.ManualSortKey;
        task.UpdatedUtc = DateTimeOffset.UtcNow;
        task.Revision++;
        await _tasks.UpdateAsync(task, cancellationToken).ConfigureAwait(false);
    }

    private async Task UndoSoftDeletedAsync(AuditEvent audit, CancellationToken cancellationToken)
    {
        var payload = DeserializeSoftDelete(audit.AfterJson);
        await _tasks.RestoreBatchAsync(payload.DeletionBatchId, cancellationToken).ConfigureAwait(false);

        var before = JsonSerializer.Deserialize<List<TaskSnapshot>>(audit.BeforeJson ?? "[]", JsonOptions) ?? [];
        foreach (var snap in before)
        {
            var task = await _tasks.GetByIdAsync(TaskId.Parse(snap.Id), cancellationToken).ConfigureAwait(false);
            if (task is null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(snap.ParentTaskId))
            {
                var parentId = TaskId.Parse(snap.ParentTaskId);
                var parent = await _tasks.GetByIdAsync(parentId, cancellationToken).ConfigureAwait(false);
                if (parent is null)
                {
                    task.ParentTaskId = null;
                    task.UpdatedUtc = DateTimeOffset.UtcNow;
                    task.Revision++;
                    await _tasks.UpdateAsync(task, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private async Task UndoDependencyAddedAsync(AuditEvent audit, CancellationToken cancellationToken)
    {
        var snap = DeserializeDependency(audit.AfterJson, "Corrupt dependency-add history.");
        var edge = await _dependencies.GetByIdAsync(DependencyId.Parse(snap.Id), cancellationToken)
            .ConfigureAwait(false);
        if (edge is null || edge.IsDeleted)
        {
            throw new BusinessException(
                ValidationCodes.UndoConflict,
                "Dependency was already removed; undo would overwrite a newer state.");
        }

        var now = DateTimeOffset.UtcNow;
        await _dependencies.SoftDeleteAsync(edge.Id, now, cancellationToken).ConfigureAwait(false);
    }

    private async Task UndoDependencyRemovedAsync(AuditEvent audit, CancellationToken cancellationToken)
    {
        var snap = DeserializeDependency(audit.BeforeJson, "Corrupt dependency-remove history.");
        var edge = await _dependencies.GetByIdAsync(DependencyId.Parse(snap.Id), cancellationToken)
            .ConfigureAwait(false);
        if (edge is null || !edge.IsDeleted)
        {
            throw new BusinessException(
                ValidationCodes.UndoConflict,
                "Dependency cannot be restored from that history entry.");
        }

        edge.DeletedUtc = null;
        await _dependencies.UpdateAsync(edge, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReopenSnapshotAsync(TaskSnapshot snap, CancellationToken cancellationToken)
    {
        var task = await RequireTaskAsync(TaskId.Parse(snap.Id), cancellationToken).ConfigureAwait(false);
        task.Status = TaskLifecycleStatus.Open;
        task.CompletedUtc = null;
        task.UpdatedUtc = DateTimeOffset.UtcNow;
        task.Revision++;
        await _tasks.UpdateAsync(task, cancellationToken).ConfigureAwait(false);
    }

    private static void ApplyEditableFields(TaskItem task, TaskSnapshot snap)
    {
        task.Title = snap.Title;
        task.Notes = snap.Notes;
        task.Priority = snap.Priority;
        task.FocusBucket = snap.FocusBucket;
        task.DeadlineLocalDate = snap.DeadlineLocalDate;
        task.DeadlineLocalTime = snap.DeadlineLocalTime;
        task.DeadlineTimeZoneId = snap.DeadlineTimeZoneId;
        task.WaitingFor = snap.WaitingFor;
        task.WaitingFollowUpUtc = snap.WaitingFollowUpUtc;
        task.ProjectId = string.IsNullOrWhiteSpace(snap.ProjectId)
            ? null
            : ProjectId.Parse(snap.ProjectId);
    }

    private async Task<TaskItem> RequireTaskAsync(TaskId id, CancellationToken cancellationToken)
    {
        var task = await _tasks.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            throw new BusinessException(ValidationCodes.TaskNotFound, $"Task {id} was not found.");
        }

        return task;
    }

    private static TaskSnapshot DeserializeSnapshot(string? json, string corruptMessage)
        => JsonSerializer.Deserialize<TaskSnapshot>(json ?? "{}", JsonOptions)
           ?? throw new BusinessException(ValidationCodes.UndoNotSupported, corruptMessage);

    private static CompletePayload DeserializeComplete(string? json)
        => JsonSerializer.Deserialize<CompletePayload>(json ?? "{}", JsonOptions)
           ?? throw new BusinessException(ValidationCodes.UndoNotSupported, "Corrupt complete history.");

    private static SoftDeletePayload DeserializeSoftDelete(string? json)
        => JsonSerializer.Deserialize<SoftDeletePayload>(json ?? "{}", JsonOptions)
           ?? throw new BusinessException(ValidationCodes.UndoNotSupported, "Corrupt delete history.");

    private static DependencySnapshot DeserializeDependency(string? json, string corruptMessage)
        => JsonSerializer.Deserialize<DependencySnapshot>(json ?? "{}", JsonOptions)
           ?? throw new BusinessException(ValidationCodes.UndoNotSupported, corruptMessage);

    public sealed class TaskSnapshot
    {
        public required string Id { get; init; }
        public string? ParentTaskId { get; init; }
        public required string Title { get; init; }
        public string? Notes { get; init; }
        public TaskPriority Priority { get; init; } = TaskPriority.Normal;
        public FocusBucket FocusBucket { get; init; }
        public double ManualSortKey { get; init; }
        public TaskLifecycleStatus Status { get; init; }
        public DateTimeOffset? CompletedUtc { get; init; }
        public DateTimeOffset? SleepUntilUtc { get; init; }
        public bool SleepIndefinitely { get; init; }
        public string? DeadlineLocalDate { get; init; }
        public string? DeadlineLocalTime { get; init; }
        public string? DeadlineTimeZoneId { get; init; }
        public string? WaitingFor { get; init; }
        public DateTimeOffset? WaitingFollowUpUtc { get; init; }
        public string? ProjectId { get; init; }
        public List<string> TagIds { get; init; } = [];
        public long Revision { get; init; }

        public static TaskSnapshot From(TaskItem task, IReadOnlyList<TagId>? tagIds = null) => new()
        {
            Id = task.Id.ToString(),
            ParentTaskId = task.ParentTaskId?.ToString(),
            Title = task.Title,
            Notes = task.Notes,
            Priority = task.Priority,
            FocusBucket = task.FocusBucket,
            ManualSortKey = task.ManualSortKey,
            Status = task.Status,
            CompletedUtc = task.CompletedUtc,
            SleepUntilUtc = task.SleepUntilUtc,
            SleepIndefinitely = task.SleepIndefinitely,
            DeadlineLocalDate = task.DeadlineLocalDate,
            DeadlineLocalTime = task.DeadlineLocalTime,
            DeadlineTimeZoneId = task.DeadlineTimeZoneId,
            WaitingFor = task.WaitingFor,
            WaitingFollowUpUtc = task.WaitingFollowUpUtc,
            ProjectId = task.ProjectId?.ToString(),
            TagIds = (tagIds ?? []).Select(t => t.ToString()).OrderBy(s => s, StringComparer.Ordinal).ToList(),
            Revision = task.Revision
        };

        public static bool ContentEquals(TaskSnapshot a, TaskSnapshot b)
            => EditableEquals(a, b)
               && string.Equals(a.ParentTaskId, b.ParentTaskId, StringComparison.Ordinal)
               && a.ManualSortKey.Equals(b.ManualSortKey)
               && a.Status == b.Status
               && a.CompletedUtc == b.CompletedUtc
               && a.SleepUntilUtc == b.SleepUntilUtc
               && a.SleepIndefinitely == b.SleepIndefinitely;

        /// <summary>
        /// Fields restored by <see cref="AuditAction.Updated"/> undo.
        /// Ignores revision so later undos that only bump revision do not false-conflict.
        /// </summary>
        public static bool EditableEquals(TaskSnapshot a, TaskSnapshot b)
            => string.Equals(a.Id, b.Id, StringComparison.Ordinal)
               && string.Equals(a.Title, b.Title, StringComparison.Ordinal)
               && string.Equals(a.Notes, b.Notes, StringComparison.Ordinal)
               && a.Priority == b.Priority
               && a.FocusBucket == b.FocusBucket
               && string.Equals(a.DeadlineLocalDate, b.DeadlineLocalDate, StringComparison.Ordinal)
               && string.Equals(a.DeadlineLocalTime, b.DeadlineLocalTime, StringComparison.Ordinal)
               && string.Equals(a.DeadlineTimeZoneId, b.DeadlineTimeZoneId, StringComparison.Ordinal)
               && string.Equals(a.WaitingFor, b.WaitingFor, StringComparison.Ordinal)
               && a.WaitingFollowUpUtc == b.WaitingFollowUpUtc
               && string.Equals(a.ProjectId, b.ProjectId, StringComparison.Ordinal)
               && a.TagIds.SequenceEqual(b.TagIds, StringComparer.Ordinal);
    }

    private sealed class DependencySnapshot
    {
        public required string Id { get; init; }
        public required string PredecessorTaskId { get; init; }
        public required string SuccessorTaskId { get; init; }
        public DependencyKind Kind { get; init; }
        public DateTimeOffset CreatedUtc { get; init; }
        public DateTimeOffset? DeletedUtc { get; init; }

        public static DependencySnapshot From(DependencyEdge edge) => new()
        {
            Id = edge.Id.ToString(),
            PredecessorTaskId = edge.PredecessorTaskId.ToString(),
            SuccessorTaskId = edge.SuccessorTaskId.ToString(),
            Kind = edge.Kind,
            CreatedUtc = edge.CreatedUtc,
            DeletedUtc = edge.DeletedUtc
        };
    }

    private sealed class CompletePayload
    {
        public ParentCompletionChoice Choice { get; init; }
        public required TaskSnapshot Task { get; init; }
        public List<TaskSnapshot> Children { get; init; } = [];
    }

    private sealed class SoftDeletePayload
    {
        public Guid DeletionBatchId { get; init; }
        public List<string> TaskIds { get; init; } = [];
    }
}
