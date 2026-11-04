using TaskPane.Core.Enums;
using TaskPane.Core.Entities;

namespace TaskPane.Application.Models;

public sealed class CompleteTaskResult
{
    public required bool Completed { get; init; }
    public required ParentCompletionChoice AppliedChoice { get; init; }
    public TaskItem? Task { get; init; }
    public int IncompleteChildCount { get; init; }
}
