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
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }
    public DateTimeOffset? DeletedUtc { get; set; }
    public Guid? DeletionBatchId { get; set; }
    public long Revision { get; set; }

    public bool IsDeleted => DeletedUtc is not null;
    public bool IsRoot => ParentTaskId is null;
    public bool IsCompleted => Status == TaskLifecycleStatus.Completed;
}
