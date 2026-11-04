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
}
