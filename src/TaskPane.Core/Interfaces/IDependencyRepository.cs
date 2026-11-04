using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using TaskPane.Core.Ids;

namespace TaskPane.Core.Interfaces;

public interface IDependencyRepository
{
    Task<DependencyEdge?> GetByIdAsync(DependencyId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DependencyEdge>> ListAsync(
        bool includeDeleted = false,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DependencyEdge>> ListForTaskAsync(
        TaskId taskId,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default);

    Task<DependencyEdge?> FindEdgeAsync(
        TaskId predecessorId,
        TaskId successorId,
        DependencyKind kind,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default);

    Task InsertAsync(DependencyEdge edge, CancellationToken cancellationToken = default);

    Task UpdateAsync(DependencyEdge edge, CancellationToken cancellationToken = default);

    Task SoftDeleteAsync(DependencyId id, DateTimeOffset deletedUtc, CancellationToken cancellationToken = default);
}
