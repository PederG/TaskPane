using System.Text.Json;
using Microsoft.Extensions.Logging;
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
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<HistoryService> _logger;

    public HistoryService(
        IAuditRepository audit,
        ITaskRepository tasks,
        IUnitOfWork unitOfWork,
        ILogger<HistoryService> logger)
    {
        _audit = audit;
        _tasks = tasks;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
        => _audit.InsertAsync(auditEvent, cancellationToken);

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
            AfterJson = JsonSerializer.Serialize(edge, JsonOptions),
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
            BeforeJson = JsonSerializer.Serialize(edge, JsonOptions),
            CorrelationId = Guid.NewGuid()
        }, cancellationToken);

    public Task<IReadOnlyList<AuditEvent>> ListRecentAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
        => _audit.ListRecentAsync(limit, cancellationToken);

    public Task<AuditEvent?> PeekUndoAsync(CancellationToken cancellationToken = default)
        => _audit.GetLatestUndoableAsync(cancellationToken);

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

        if (audit.IsUndone)
        {
            throw new BusinessException(ValidationCodes.UndoAlreadyApplied, "That change was already undone.");
        }

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            switch (audit.Action)
            {
                case AuditAction.Completed:
                    await UndoCompletedAsync(audit, ct).ConfigureAwait(false);
                    break;
                case AuditAction.Slept:
                    await UndoSleptAsync(audit, ct).ConfigureAwait(false);
                    break;
                case AuditAction.Moved:
                    await UndoMovedAsync(audit, ct).ConfigureAwait(false);
                    break;
                case AuditAction.SoftDeleted:
                    await UndoSoftDeletedAsync(audit, ct).ConfigureAwait(false);
                    break;
                default:
                    throw new BusinessException(
                        ValidationCodes.UndoNotSupported,
                        $"Undo is not supported for {audit.Action}.");
            }

            audit.IsUndone = true;
            await _audit.UpdateAsync(audit, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Undid audit {AuditId} action={Action}", audit.Id, audit.Action);
        return audit;
    }

    private async Task UndoCompletedAsync(AuditEvent audit, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<CompletePayload>(audit.AfterJson ?? "{}", JsonOptions)
                      ?? throw new BusinessException(ValidationCodes.UndoNotSupported, "Corrupt complete history.");

        await ReopenSnapshotAsync(payload.Task, cancellationToken).ConfigureAwait(false);
        foreach (var child in payload.Children)
        {
            await ReopenSnapshotAsync(child, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task UndoSleptAsync(AuditEvent audit, CancellationToken cancellationToken)
    {
        var before = JsonSerializer.Deserialize<TaskSnapshot>(audit.BeforeJson ?? "{}", JsonOptions)
                     ?? throw new BusinessException(ValidationCodes.UndoNotSupported, "Corrupt sleep history.");
        var task = await RequireTaskAsync(TaskId.Parse(before.Id), cancellationToken).ConfigureAwait(false);
        task.SleepIndefinitely = before.SleepIndefinitely;
        task.SleepUntilUtc = before.SleepUntilUtc;
        task.UpdatedUtc = DateTimeOffset.UtcNow;
        task.Revision++;
        await _tasks.UpdateAsync(task, cancellationToken).ConfigureAwait(false);
    }

    private async Task UndoMovedAsync(AuditEvent audit, CancellationToken cancellationToken)
    {
        var before = JsonSerializer.Deserialize<TaskSnapshot>(audit.BeforeJson ?? "{}", JsonOptions)
                     ?? throw new BusinessException(ValidationCodes.UndoNotSupported, "Corrupt move history.");
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
        var payload = JsonSerializer.Deserialize<SoftDeletePayload>(audit.AfterJson ?? "{}", JsonOptions)
                      ?? throw new BusinessException(ValidationCodes.UndoNotSupported, "Corrupt delete history.");
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

    private async Task ReopenSnapshotAsync(TaskSnapshot snap, CancellationToken cancellationToken)
    {
        var task = await RequireTaskAsync(TaskId.Parse(snap.Id), cancellationToken).ConfigureAwait(false);
        task.Status = TaskLifecycleStatus.Open;
        task.CompletedUtc = null;
        task.UpdatedUtc = DateTimeOffset.UtcNow;
        task.Revision++;
        await _tasks.UpdateAsync(task, cancellationToken).ConfigureAwait(false);
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

    public sealed class TaskSnapshot
    {
        public required string Id { get; init; }
        public string? ParentTaskId { get; init; }
        public required string Title { get; init; }
        public FocusBucket FocusBucket { get; init; }
        public double ManualSortKey { get; init; }
        public TaskLifecycleStatus Status { get; init; }
        public DateTimeOffset? CompletedUtc { get; init; }
        public DateTimeOffset? SleepUntilUtc { get; init; }
        public bool SleepIndefinitely { get; init; }

        public static TaskSnapshot From(TaskItem task) => new()
        {
            Id = task.Id.ToString(),
            ParentTaskId = task.ParentTaskId?.ToString(),
            Title = task.Title,
            FocusBucket = task.FocusBucket,
            ManualSortKey = task.ManualSortKey,
            Status = task.Status,
            CompletedUtc = task.CompletedUtc,
            SleepUntilUtc = task.SleepUntilUtc,
            SleepIndefinitely = task.SleepIndefinitely
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
