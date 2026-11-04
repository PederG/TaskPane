using TaskPane.Core.Entities;
using TaskPane.Core.Ids;

namespace TaskPane.Core.Interfaces;

public interface ITagRepository
{
    Task<Tag?> GetByIdAsync(TagId id, CancellationToken cancellationToken = default);

    Task<Tag?> FindByNameAsync(string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Tag>> ListAsync(CancellationToken cancellationToken = default);

    Task InsertAsync(Tag tag, CancellationToken cancellationToken = default);

    Task UpdateAsync(Tag tag, CancellationToken cancellationToken = default);

    Task SetTaskTagsAsync(
        TaskId taskId,
        IReadOnlyList<TagId> tagIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TagId>> GetTagIdsForTaskAsync(
        TaskId taskId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<TaskId, IReadOnlyList<TagId>>> GetTagIdsForTasksAsync(
        IReadOnlyList<TaskId> taskIds,
        CancellationToken cancellationToken = default);
}
