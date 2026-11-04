using TaskPane.Core.Entities;
using TaskPane.Core.Ids;

namespace TaskPane.Core.Interfaces;

public interface IProjectRepository
{
    Task<Project?> GetByIdAsync(ProjectId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Project>> ListAsync(
        bool includeArchived = false,
        CancellationToken cancellationToken = default);

    Task InsertAsync(Project project, CancellationToken cancellationToken = default);

    Task UpdateAsync(Project project, CancellationToken cancellationToken = default);
}
