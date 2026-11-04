using TaskPane.Core.Ids;

namespace TaskPane.Core.Entities;

public sealed class RecurrenceTemplate
{
    public required RecurrenceTemplateId Id { get; init; }

    /// <summary>Source task that defined the series (root or one-level subtask).</summary>
    public required TaskId SourceTaskId { get; set; }

    public required string RuleJson { get; set; }
    public required string ContentSnapshotJson { get; set; }
    public required string TimeZoneId { get; set; }
    public DateTimeOffset? NextEvaluationUtc { get; set; }
    public bool IsEnabled { get; set; } = true;

    /// <summary>Most recent occurrence key that was intentionally skipped after absence (informational).</summary>
    public string? LastSkippedOccurrenceKey { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}
