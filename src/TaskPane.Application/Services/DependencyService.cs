using Microsoft.Extensions.Logging;
using TaskPane.Application.Models;
using TaskPane.Core.Dependencies;
using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using TaskPane.Core.Errors;
using TaskPane.Core.Ids;
using TaskPane.Core.Interfaces;
using TaskPane.Core.Validation;

namespace TaskPane.Application.Services;

public sealed class DependencyService
{
    private readonly IDependencyRepository _dependencies;
    private readonly ITaskRepository _tasks;
    private readonly IUnitOfWork _unitOfWork;
    private readonly HistoryService _history;
    private readonly ILogger<DependencyService> _logger;

    public DependencyService(
        IDependencyRepository dependencies,
        ITaskRepository tasks,
        IUnitOfWork unitOfWork,
        HistoryService history,
        ILogger<DependencyService> logger)
    {
        _dependencies = dependencies;
        _tasks = tasks;
        _unitOfWork = unitOfWork;
        _history = history;
        _logger = logger;
    }

    public async Task<DependencyEdge> AddRequiredAsync(
        TaskId predecessorId,
        TaskId successorId,
        CancellationToken cancellationToken = default)
        => await AddAsync(predecessorId, successorId, DependencyKind.Required, cancellationToken)
            .ConfigureAwait(false);

    public async Task<DependencyEdge> AddAsync(
        TaskId predecessorId,
        TaskId successorId,
        DependencyKind kind,
        CancellationToken cancellationToken = default)
    {
        if (predecessorId.Equals(successorId))
        {
            throw new BusinessException(
                ValidationCodes.DependencySelfEdge,
                "A task cannot depend on itself.");
        }

        var predecessor = await _tasks.GetByIdAsync(predecessorId, cancellationToken).ConfigureAwait(false);
        TaskHierarchyValidator.EnsureExists(predecessor, predecessorId);
        var successor = await _tasks.GetByIdAsync(successorId, cancellationToken).ConfigureAwait(false);
        TaskHierarchyValidator.EnsureExists(successor, successorId);

        var existing = await _dependencies
            .FindEdgeAsync(predecessorId, successorId, kind, includeDeleted: true, cancellationToken)
            .ConfigureAwait(false);
        if (existing is { IsDeleted: false })
        {
            throw new BusinessException(
                ValidationCodes.DependencyDuplicate,
                "That dependency already exists.");
        }

        if (kind == DependencyKind.Required)
        {
            var required = await ListRequiredEdgesAsync(cancellationToken).ConfigureAwait(false);
            if (DependencyGraph.WouldCreateCycle(required, predecessorId, successorId))
            {
                throw new BusinessException(
                    ValidationCodes.DependencyCycle,
                    "That dependency would create a cycle.");
            }
        }

        var now = DateTimeOffset.UtcNow;
        DependencyEdge edge;
        if (existing is { IsDeleted: true })
        {
            existing.DeletedUtc = null;
            existing.CreatedUtc = now;
            edge = existing;
            await _unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                await _dependencies.UpdateAsync(edge, ct).ConfigureAwait(false);
                await _history.RecordDependencyAddedAsync(edge, ct).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            edge = new DependencyEdge
            {
                Id = DependencyId.New(),
                PredecessorTaskId = predecessorId,
                SuccessorTaskId = successorId,
                Kind = kind,
                CreatedUtc = now
            };
            await _unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                await _dependencies.InsertAsync(edge, ct).ConfigureAwait(false);
                await _history.RecordDependencyAddedAsync(edge, ct).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Added {Kind} dependency {Pred} -> {Succ}",
            kind,
            predecessorId,
            successorId);
        return edge;
    }

    public async Task RemoveAsync(DependencyId edgeId, CancellationToken cancellationToken = default)
    {
        var edge = await _dependencies.GetByIdAsync(edgeId, cancellationToken).ConfigureAwait(false);
        if (edge is null || edge.IsDeleted)
        {
            throw new BusinessException(ValidationCodes.DependencyNotFound, "Dependency was not found.");
        }

        var now = DateTimeOffset.UtcNow;
        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await _dependencies.SoftDeleteAsync(edge.Id, now, ct).ConfigureAwait(false);
            edge.DeletedUtc = now;
            await _history.RecordDependencyRemovedAsync(edge, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BlockedState> GetBlockedStateAsync(
        TaskId taskId,
        CancellationToken cancellationToken = default)
    {
        var edges = await _dependencies.ListForTaskAsync(taskId, includeDeleted: false, cancellationToken)
            .ConfigureAwait(false);
        var tasks = await _tasks.ListAsync(includeDeleted: true, cancellationToken).ConfigureAwait(false);
        var byId = tasks.ToDictionary(t => t.Id);
        return DependencyGraph.EvaluateBlocked(taskId, edges, byId);
    }

    public async Task<IReadOnlyDictionary<TaskId, BlockedState>> GetBlockedStatesAsync(
        IReadOnlyList<TaskId> taskIds,
        CancellationToken cancellationToken = default)
    {
        var edges = await _dependencies.ListAsync(includeDeleted: false, cancellationToken).ConfigureAwait(false);
        var tasks = await _tasks.ListAsync(includeDeleted: true, cancellationToken).ConfigureAwait(false);
        var byId = tasks.ToDictionary(t => t.Id);
        return taskIds.ToDictionary(
            id => id,
            id => DependencyGraph.EvaluateBlocked(id, edges, byId));
    }

    public async Task<TaskDependencyInfo> GetTaskDependencyInfoAsync(
        TaskId taskId,
        CancellationToken cancellationToken = default)
    {
        var edges = await _dependencies.ListForTaskAsync(taskId, includeDeleted: false, cancellationToken)
            .ConfigureAwait(false);
        var tasks = await _tasks.ListAsync(includeDeleted: true, cancellationToken).ConfigureAwait(false);
        var byId = tasks.ToDictionary(t => t.Id);
        var blocked = DependencyGraph.EvaluateBlocked(taskId, edges, byId);

        var dependsOn = edges
            .Where(e => e.SuccessorTaskId.Equals(taskId))
            .Select(e => ToLink(e, e.PredecessorTaskId, byId))
            .OrderBy(l => l.OtherTitle, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var blocks = edges
            .Where(e => e.PredecessorTaskId.Equals(taskId))
            .Select(e => ToLink(e, e.SuccessorTaskId, byId))
            .OrderBy(l => l.OtherTitle, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new TaskDependencyInfo
        {
            TaskId = taskId,
            Blocked = blocked,
            DependsOn = dependsOn,
            Blocks = blocks
        };
    }

    public async Task<IReadOnlyList<TaskItem>> ListRecycleBinAsync(CancellationToken cancellationToken = default)
    {
        var all = await _tasks.ListAsync(includeDeleted: true, cancellationToken).ConfigureAwait(false);
        return all.Where(t => t.IsDeleted).OrderByDescending(t => t.DeletedUtc).ToList();
    }

    public async Task<IReadOnlyList<RecycleBinEntry>> ListRecycleBinEntriesAsync(
        CancellationToken cancellationToken = default)
    {
        var all = await _tasks.ListAsync(includeDeleted: true, cancellationToken).ConfigureAwait(false);
        var byId = all.ToDictionary(t => t.Id);
        var deleted = all.Where(t => t.IsDeleted).OrderByDescending(t => t.DeletedUtc).ToList();
        var edges = await _dependencies.ListAsync(includeDeleted: false, cancellationToken).ConfigureAwait(false);

        return deleted.Select(task =>
        {
            string? warning = null;
            var parentMissing = false;
            if (task.ParentTaskId is { } parentId)
            {
                if (!byId.TryGetValue(parentId, out var parent))
                {
                    parentMissing = true;
                    warning = "Parent task is missing; restore will promote this item to a root.";
                }
                else if (parent.IsDeleted && parent.DeletionBatchId != task.DeletionBatchId)
                {
                    parentMissing = true;
                    warning = "Parent is still in the recycle bin (different deletion).";
                }
            }

            var deletedPreds = edges.Count(e =>
                e.Kind == DependencyKind.Required
                && e.SuccessorTaskId.Equals(task.Id)
                && (!byId.TryGetValue(e.PredecessorTaskId, out var pred) || pred.IsDeleted));
            if (deletedPreds > 0)
            {
                var depWarning = $"{deletedPreds} required predecessor(s) deleted or missing.";
                warning = warning is null ? depWarning : $"{warning} {depWarning}";
            }

            return new RecycleBinEntry
            {
                Task = task,
                DeletionBatchId = task.DeletionBatchId,
                ParentMissingOrDeleted = parentMissing,
                Warning = warning
            };
        }).ToList();
    }

    private async Task<IReadOnlyList<DependencyEdge>> ListRequiredEdgesAsync(CancellationToken cancellationToken)
    {
        var all = await _dependencies.ListAsync(includeDeleted: false, cancellationToken).ConfigureAwait(false);
        return all.Where(e => e.Kind == DependencyKind.Required).ToList();
    }

    private static DependencyLinkView ToLink(
        DependencyEdge edge,
        TaskId otherId,
        IReadOnlyDictionary<TaskId, TaskItem> byId)
    {
        byId.TryGetValue(otherId, out var other);
        return new DependencyLinkView
        {
            EdgeId = edge.Id,
            OtherTaskId = otherId,
            OtherTitle = other?.Title ?? "(missing task)",
            Kind = edge.Kind,
            OtherIsCompleted = other?.IsCompleted == true,
            OtherIsDeleted = other?.IsDeleted == true,
            OtherIsMissing = other is null
        };
    }
}
