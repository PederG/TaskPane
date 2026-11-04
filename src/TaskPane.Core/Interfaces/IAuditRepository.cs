using TaskPane.Core.Entities;
using TaskPane.Core.Ids;

namespace TaskPane.Core.Interfaces;

public interface IAuditRepository
{
    Task InsertAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);

    Task UpdateAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);

    Task<AuditEvent?> GetByIdAsync(AuditEventId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuditEvent>> ListRecentAsync(
        int limit = 100,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuditEvent>> ListForEntityAsync(
        string entityType,
        string entityId,
        int limit = 50,
        CancellationToken cancellationToken = default);

    Task<AuditEvent?> GetLatestUndoableAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuditEvent>> ListAllAsync(CancellationToken cancellationToken = default);
}
