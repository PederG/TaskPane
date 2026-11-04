using TaskPane.Core.Enums;
using TaskPane.Core.Transfer;

namespace TaskPane.Application.Models;

public sealed class ImportPreview
{
    public required ExportDocument Document { get; init; }
    public int TaskCount { get; init; }
    public int SubtaskCount { get; init; }
    public int DeletedTaskCount { get; init; }
    public int DependencyCount { get; init; }
    public int ProjectCount { get; init; }
    public int TagCount { get; init; }
    public int SessionCount { get; init; }
    public int RecurrenceTemplateCount { get; init; }
    public int ReminderCount { get; init; }
    public int AuditEventCount { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
    public bool IsValid => Errors.Count == 0;
}

public sealed class ImportResult
{
    public required ImportMode Mode { get; init; }
    public int ImportedTasks { get; init; }
    public int SkippedTasks { get; init; }
    public int ImportedDependencies { get; init; }
    public int ImportedSessions { get; init; }
    public int ImportedProjects { get; init; }
    public int ImportedTags { get; init; }
    public int ImportedReminders { get; init; }
    public int ImportedRecurrenceTemplates { get; init; }
    public int ImportedAuditEvents { get; init; }
    public string? SafetyBackupPath { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = [];
}
