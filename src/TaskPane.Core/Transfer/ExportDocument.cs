namespace TaskPane.Core.Transfer;

/// <summary>Versioned JSON export envelope. Canonical round-trip format for Phase 6.</summary>
public sealed class ExportDocument
{
    public const int CurrentSchemaVersion = 1;
    public const string ApplicationName = "TaskPane";

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Application { get; set; } = ApplicationName;
    public DateTimeOffset ExportedAtUtc { get; set; }
    public List<ExportTaskDto> Tasks { get; set; } = [];
    public List<ExportDependencyDto> Dependencies { get; set; } = [];
    public List<ExportTimeSessionDto> TimeSessions { get; set; } = [];
    public List<ExportProjectDto> Projects { get; set; } = [];
    public List<ExportTagDto> Tags { get; set; } = [];
    public List<ExportTaskTagDto> TaskTags { get; set; } = [];
    public List<ExportRecurrenceTemplateDto> RecurrenceTemplates { get; set; } = [];
    public List<ExportReminderDto> Reminders { get; set; } = [];
    public List<ExportAuditEventDto> AuditEvents { get; set; } = [];
}

public sealed class ExportTaskDto
{
    public string Id { get; set; } = "";
    public string? ParentTaskId { get; set; }
    public string Title { get; set; } = "";
    public string? Notes { get; set; }
    public int Priority { get; set; }
    public int FocusBucket { get; set; }
    public int Status { get; set; }
    public double ManualSortKey { get; set; }
    public string? DeadlineLocalDate { get; set; }
    public string? DeadlineLocalTime { get; set; }
    public string? DeadlineTimeZoneId { get; set; }
    public DateTimeOffset? SleepUntilUtc { get; set; }
    public bool SleepIndefinitely { get; set; }
    public string? WaitingFor { get; set; }
    public DateTimeOffset? WaitingFollowUpUtc { get; set; }
    public string? ProjectId { get; set; }
    public string? RecurrenceTemplateId { get; set; }
    public string? OccurrenceKey { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }
    public DateTimeOffset? DeletedUtc { get; set; }
    public string? DeletionBatchId { get; set; }
    public long Revision { get; set; }
}

public sealed class ExportDependencyDto
{
    public string Id { get; set; } = "";
    public string PredecessorTaskId { get; set; } = "";
    public string SuccessorTaskId { get; set; } = "";
    public int Kind { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? DeletedUtc { get; set; }
}

public sealed class ExportTimeSessionDto
{
    public string Id { get; set; } = "";
    public string TaskId { get; set; } = "";
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset? EndedUtc { get; set; }
    public int? EndReason { get; set; }
    public bool IsDiscarded { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}

public sealed class ExportProjectDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Color { get; set; }
    public DateTimeOffset? ArchivedUtc { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}

public sealed class ExportTagDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Color { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
}

public sealed class ExportTaskTagDto
{
    public string TaskId { get; set; } = "";
    public string TagId { get; set; } = "";
}

public sealed class ExportRecurrenceTemplateDto
{
    public string Id { get; set; } = "";
    public string SourceTaskId { get; set; } = "";
    public string RuleJson { get; set; } = "";
    public string ContentSnapshotJson { get; set; } = "";
    public string TimeZoneId { get; set; } = "";
    public DateTimeOffset? NextEvaluationUtc { get; set; }
    public bool IsEnabled { get; set; }
    public string? LastSkippedOccurrenceKey { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}

public sealed class ExportReminderDto
{
    public string Id { get; set; } = "";
    public string TaskId { get; set; } = "";
    public DateTimeOffset DueUtc { get; set; }
    public DateTimeOffset? DeliveredUtc { get; set; }
    public DateTimeOffset? DismissedUtc { get; set; }
    public DateTimeOffset? SnoozedUntilUtc { get; set; }
    public int Kind { get; set; }
    public string DeliveryKey { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; }
}

public sealed class ExportAuditEventDto
{
    public string Id { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public int Action { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? CorrelationId { get; set; }
    public bool IsUndone { get; set; }
}
