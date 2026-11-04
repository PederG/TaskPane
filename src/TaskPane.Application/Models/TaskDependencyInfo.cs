using TaskPane.Core.Dependencies;
using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using TaskPane.Core.Ids;

namespace TaskPane.Application.Models;

public sealed class TaskDependencyInfo
{
    public required TaskId TaskId { get; init; }
    public required BlockedState Blocked { get; init; }
    public required IReadOnlyList<DependencyLinkView> DependsOn { get; init; }
    public required IReadOnlyList<DependencyLinkView> Blocks { get; init; }
}

public sealed class DependencyLinkView
{
    public required DependencyId EdgeId { get; init; }
    public required TaskId OtherTaskId { get; init; }
    public required string OtherTitle { get; init; }
    public required DependencyKind Kind { get; init; }
    public bool OtherIsCompleted { get; init; }
    public bool OtherIsDeleted { get; init; }
    public bool OtherIsMissing { get; init; }
}

public sealed class RecycleBinEntry
{
    public required TaskItem Task { get; init; }
    public required Guid? DeletionBatchId { get; init; }
    public bool ParentMissingOrDeleted { get; init; }
    public string? Warning { get; init; }
}
