using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using TaskPane.Core.Ids;

namespace TaskPane.Core.Interfaces;

public interface ITaskRepository
{
    Task<TaskItem?> GetByIdAsync(TaskId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaskItem>> ListAsync(
        bool includeDeleted = false,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaskItem>> ListChildrenAsync(
        TaskId parentId,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default);

    Task InsertAsync(TaskItem task, CancellationToken cancellationToken = default);

    Task UpdateAsync(TaskItem task, CancellationToken cancellationToken = default);

    Task SoftDeleteBatchAsync(
        IReadOnlyList<TaskId> ids,
        Guid deletionBatchId,
        DateTimeOffset deletedUtc,
        CancellationToken cancellationToken = default);

    Task RestoreBatchAsync(
        Guid deletionBatchId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaskItem>> ListByDeletionBatchAsync(
        Guid deletionBatchId,
        CancellationToken cancellationToken = default);

    Task<double> GetNextSortKeyAsync(
        TaskId? parentId,
        FocusBucket? focusBucket,
        CancellationToken cancellationToken = default);
}
