using TaskPane.Core.Enums;

namespace TaskPane.Core.Recurrence;

/// <summary>Template content cloned into each new occurrence.</summary>
public sealed class RecurrenceContentSnapshot
{
    public required string Title { get; set; }
    public string? Notes { get; set; }
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;
    public FocusBucket FocusBucket { get; set; } = FocusBucket.Inbox;
    public string? ProjectId { get; set; }
    public bool CopyNotes { get; set; } = true;
    public bool CopySubtasks { get; set; } = true;
    public bool SetDeadlineToOccurrenceDate { get; set; } = true;
    public List<RecurrenceSubtaskSnapshot> Subtasks { get; set; } = [];
}

public sealed class RecurrenceSubtaskSnapshot
{
    public required string Title { get; set; }
    public string? Notes { get; set; }
}
