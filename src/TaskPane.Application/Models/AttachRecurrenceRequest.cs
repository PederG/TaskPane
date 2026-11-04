using TaskPane.Core.Ids;
using TaskPane.Core.Recurrence;

namespace TaskPane.Application.Models;

public sealed class AttachRecurrenceRequest
{
    public required TaskId TaskId { get; init; }
    public required RecurrenceRule Rule { get; init; }
    public string? TimeZoneId { get; init; }
    public bool CopyNotes { get; init; } = true;
    public bool CopySubtasks { get; init; } = true;
    public bool SetDeadlineToOccurrenceDate { get; init; } = true;

    /// <summary>
    /// When true, converts the source task into the first occurrence (anchor key)
    /// instead of leaving a separate seed task.
    /// </summary>
    public bool ConvertSourceToFirstOccurrence { get; init; } = true;
}
