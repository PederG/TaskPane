using TaskPane.Core.Enums;
using TaskPane.Core.Ids;

namespace TaskPane.Core.Entities;

public sealed class TaskItem
{
    public required TaskId Id { get; init; }
    public TaskId? ParentTaskId { get; set; }
    public required string Title { get; set; }
    public string? Notes { get; set; }
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;
    public FocusBucket FocusBucket { get; set; } = FocusBucket.Inbox;
    public TaskLifecycleStatus Status { get; set; } = TaskLifecycleStatus.Open;
    public double ManualSortKey { get; set; }

    /// <summary>ISO calendar date (YYYY-MM-DD). Never treat as midnight UTC.</summary>
    public string? DeadlineLocalDate { get; set; }

    /// <summary>Optional local time (HH:mm or HH:mm:ss). Null means date-only deadline.</summary>
    public string? DeadlineLocalTime { get; set; }

    public string? DeadlineTimeZoneId { get; set; }
    public DateTimeOffset? SleepUntilUtc { get; set; }
    public bool SleepIndefinitely { get; set; }
    public string? WaitingFor { get; set; }
    public DateTimeOffset? WaitingFollowUpUtc { get; set; }
    public ProjectId? ProjectId { get; set; }
    public RecurrenceTemplateId? RecurrenceTemplateId { get; set; }
    public string? OccurrenceKey { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }
    public DateTimeOffset? DeletedUtc { get; set; }
    public Guid? DeletionBatchId { get; set; }
    public long Revision { get; set; }

    public bool IsDeleted => DeletedUtc is not null;
    public bool IsRoot => ParentTaskId is null;
    public bool IsCompleted => Status == TaskLifecycleStatus.Completed;
    public bool HasDeadline => !string.IsNullOrWhiteSpace(DeadlineLocalDate);
    public bool IsWaiting => !string.IsNullOrWhiteSpace(WaitingFor);
    public bool IsRecurringOccurrence => RecurrenceTemplateId is not null && !string.IsNullOrWhiteSpace(OccurrenceKey);
}
