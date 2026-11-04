using CommunityToolkit.Mvvm.ComponentModel;
using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using TaskPane.Core.Ids;

namespace TaskPane.App.ViewModels;

public partial class HistoryRowViewModel : ObservableObject
{
    public HistoryRowViewModel(AuditEvent auditEvent)
    {
        Id = auditEvent.Id;
        Action = auditEvent.Action;
        EntityType = auditEvent.EntityType;
        EntityId = auditEvent.EntityId;
        TimestampUtc = auditEvent.TimestampUtc;
        IsUndone = auditEvent.IsUndone;
        IsUndoable = !auditEvent.IsUndone && auditEvent.Action is
            AuditAction.Completed or AuditAction.Slept or AuditAction.Moved or AuditAction.SoftDeleted;
        Display = $"{auditEvent.TimestampUtc:u} · {auditEvent.Action} · {auditEvent.EntityType} {Truncate(auditEvent.EntityId)}"
                  + (auditEvent.IsUndone ? " · undone" : string.Empty);
    }

    public AuditEventId Id { get; }
    public AuditAction Action { get; }
    public string EntityType { get; }
    public string EntityId { get; }
    public DateTimeOffset TimestampUtc { get; }
    public bool IsUndone { get; }
    public bool IsUndoable { get; }
    public string Display { get; }

    private static string Truncate(string value)
        => value.Length <= 8 ? value : value[..8];
}
