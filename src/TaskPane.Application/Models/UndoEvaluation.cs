using TaskPane.Core.Enums;
using TaskPane.Core.Ids;

namespace TaskPane.Application.Models;

/// <summary>Clear can/cannot-undo result for a history entry.</summary>
public sealed class UndoEvaluation
{
    public required AuditEventId AuditEventId { get; init; }
    public required AuditAction Action { get; init; }
    public required bool CanUndo { get; init; }
    public required string Reason { get; init; }

    public static UndoEvaluation Allow(AuditEventId id, AuditAction action)
        => new()
        {
            AuditEventId = id,
            Action = action,
            CanUndo = true,
            Reason = "Ready to undo."
        };

    public static UndoEvaluation Deny(AuditEventId id, AuditAction action, string reason)
        => new()
        {
            AuditEventId = id,
            Action = action,
            CanUndo = false,
            Reason = reason
        };
}
