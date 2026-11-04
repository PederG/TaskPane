using TaskPane.Core.Entities;
using TaskPane.Core.Ids;

namespace TaskPane.Core.Interfaces;

public interface ITimeSessionRepository
{
    Task<TimeSession?> GetByIdAsync(SessionId id, CancellationToken cancellationToken = default);

    Task<TimeSession?> GetOpenSessionAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TimeSession>> ListForTaskAsync(
        TaskId taskId,
        bool includeDiscarded = false,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TimeSession>> ListForTasksAsync(
        IReadOnlyList<TaskId> taskIds,
        bool includeDiscarded = false,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TimeSession>> ListInRangeAsync(
        DateTimeOffset rangeStartUtc,
        DateTimeOffset rangeEndUtc,
        bool includeDiscarded = false,
        CancellationToken cancellationToken = default);

    Task InsertAsync(TimeSession session, CancellationToken cancellationToken = default);

    Task UpdateAsync(TimeSession session, CancellationToken cancellationToken = default);

    Task DeleteAsync(SessionId id, CancellationToken cancellationToken = default);
}
