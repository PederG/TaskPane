using TaskPane.Core.Entities;

namespace TaskPane.Core.Interfaces;

public interface IBackupHistoryRepository
{
    Task InsertAsync(BackupHistoryEntry entry, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BackupHistoryEntry>> ListRecentAsync(
        int limit = 20,
        CancellationToken cancellationToken = default);

    Task<BackupHistoryEntry?> GetLatestSuccessfulAsync(CancellationToken cancellationToken = default);
}
