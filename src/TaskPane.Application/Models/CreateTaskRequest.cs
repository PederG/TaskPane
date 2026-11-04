using TaskPane.Core.Enums;
using TaskPane.Core.Ids;

namespace TaskPane.Application.Models;

public sealed class CreateTaskRequest
{
    public required string Title { get; init; }
    public string? Notes { get; init; }
    public TaskId? ParentTaskId { get; init; }
    public FocusBucket FocusBucket { get; init; } = FocusBucket.Inbox;
    public TaskPriority Priority { get; init; } = TaskPriority.Normal;
    public string? DeadlineLocalDate { get; init; }
    public string? DeadlineLocalTime { get; init; }
    public string? DeadlineTimeZoneId { get; init; }
    public ProjectId? ProjectId { get; init; }
}
