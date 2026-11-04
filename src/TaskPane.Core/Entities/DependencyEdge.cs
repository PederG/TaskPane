using TaskPane.Core.Enums;
using TaskPane.Core.Ids;

namespace TaskPane.Core.Entities;

public sealed class DependencyEdge
{
    public required DependencyId Id { get; init; }
    public required TaskId PredecessorTaskId { get; set; }
    public required TaskId SuccessorTaskId { get; set; }
    public DependencyKind Kind { get; set; } = DependencyKind.Required;
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? DeletedUtc { get; set; }

    public bool IsDeleted => DeletedUtc is not null;
}
