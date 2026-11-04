using TaskPane.Core.Enums;
using TaskPane.Core.Ids;

namespace TaskPane.Application.Models;

public sealed class UpdateTaskRequest
{
    public required TaskId Id { get; init; }
    public required string Title { get; init; }
    public string? Notes { get; init; }
    public TaskPriority Priority { get; init; }
    public FocusBucket FocusBucket { get; init; }
    public string? DeadlineLocalDate { get; init; }
    public string? DeadlineLocalTime { get; init; }
    public string? DeadlineTimeZoneId { get; init; }
    public string? WaitingFor { get; init; }
    public DateTimeOffset? WaitingFollowUpUtc { get; init; }
    public ProjectId? ProjectId { get; init; }
    public IReadOnlyList<TagId>? TagIds { get; init; }
}
