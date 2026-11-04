using TaskPane.Core.Entities;
using TaskPane.Core.Ids;

namespace TaskPane.Core.Interfaces;

public interface IReminderRepository
{
    Task<Reminder?> GetByIdAsync(ReminderId id, CancellationToken cancellationToken = default);

    Task<Reminder?> GetByDeliveryKeyAsync(string deliveryKey, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Reminder>> ListForTaskAsync(TaskId taskId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Reminder>> ListDueAsync(DateTimeOffset utcNow, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Reminder>> ListAllAsync(CancellationToken cancellationToken = default);

    Task InsertAsync(Reminder reminder, CancellationToken cancellationToken = default);

    Task UpdateAsync(Reminder reminder, CancellationToken cancellationToken = default);

    Task DeleteAsync(ReminderId id, CancellationToken cancellationToken = default);
}
