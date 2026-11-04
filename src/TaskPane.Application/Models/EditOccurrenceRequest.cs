using TaskPane.Core.Enums;
using TaskPane.Core.Ids;
using TaskPane.Core.Recurrence;

namespace TaskPane.Application.Models;

public sealed class EditOccurrenceRequest
{
    public required TaskId TaskId { get; init; }
    public OccurrenceEditScope Scope { get; init; } = OccurrenceEditScope.ThisOccurrenceOnly;
    public string? Title { get; init; }
    public string? Notes { get; init; }
    public RecurrenceRule? FutureRule { get; init; }
    public bool? CopyNotes { get; init; }
    public bool? CopySubtasks { get; init; }
}
