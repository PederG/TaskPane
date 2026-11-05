using CommunityToolkit.Mvvm.ComponentModel;
using TaskPane.Application.Models;
using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using TaskPane.Core.Ids;
using TaskPane.Application.Services;

namespace TaskPane.App.ViewModels;

public partial class HistoryRowViewModel : ObservableObject
{
    public HistoryRowViewModel(AuditEvent auditEvent, UndoEvaluation? evaluation = null)
    {
        Id = auditEvent.Id;
        Action = auditEvent.Action;
        EntityType = auditEvent.EntityType;
        EntityId = auditEvent.EntityId;
        TimestampUtc = auditEvent.TimestampUtc;
        IsUndone = auditEvent.IsUndone;

        var canUndoByAction = !auditEvent.IsUndone && HistoryService.IsUndoableAction(auditEvent.Action);
        IsUndoable = evaluation?.CanUndo ?? canUndoByAction;
        CannotUndoReason = evaluation is { CanUndo: false }
            ? evaluation.Reason
            : auditEvent.IsUndone
                ? "Already undone."
                : canUndoByAction
                    ? string.Empty
                    : $"Undo is not supported for {auditEvent.Action}.";

        var suffix = auditEvent.IsUndone
            ? " · undone"
            : IsUndoable
                ? string.Empty
                : string.IsNullOrWhiteSpace(CannotUndoReason)
                    ? " · cannot undo"
                    : $" · cannot undo ({CannotUndoReason})";

        Display = $"{auditEvent.TimestampUtc:u} · {auditEvent.Action} · {auditEvent.EntityType} {Truncate(auditEvent.EntityId)}"
                  + suffix;
    }

    public AuditEventId Id { get; }
    public AuditAction Action { get; }
    public string EntityType { get; }
    public string EntityId { get; }
    public DateTimeOffset TimestampUtc { get; }
    public bool IsUndone { get; }
    public bool IsUndoable { get; }
    public string CannotUndoReason { get; }
    public string Display { get; }

    private static string Truncate(string value)
        => value.Length <= 8 ? value : value[..8];
}
