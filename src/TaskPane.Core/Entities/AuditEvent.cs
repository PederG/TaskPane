using TaskPane.Core.Enums;
using TaskPane.Core.Ids;

namespace TaskPane.Core.Entities;

public sealed class AuditEvent
{
    public required AuditEventId Id { get; init; }
    public required string EntityType { get; init; }
    public required string EntityId { get; init; }
    public required AuditAction Action { get; init; }
    public DateTimeOffset TimestampUtc { get; init; }
    public string? BeforeJson { get; init; }
    public string? AfterJson { get; init; }
    public Guid? CorrelationId { get; init; }
    public bool IsUndone { get; set; }
}
