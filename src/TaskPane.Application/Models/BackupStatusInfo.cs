using TaskPane.Core.Enums;

namespace TaskPane.Application.Models;

public sealed class BackupStatusInfo
{
    public DateTimeOffset? LastSuccessfulUtc { get; init; }
    public BackupOutcome? LastOutcome { get; init; }
    public string? LastLocalPath { get; init; }
    public string? LastDocumentsPath { get; init; }
    public bool DocumentsAvailable { get; init; }
    public bool DocumentsIsUnc { get; init; }
    public string? DocumentsWarning { get; init; }
    public string StatusText { get; init; } = "No backups yet.";
}

public sealed class BackupRunResult
{
    public required BackupOutcome Outcome { get; init; }
    public required string LocalPath { get; init; }
    public string? DocumentsPath { get; init; }
    public string? ErrorCode { get; init; }
    public string Message { get; init; } = "";
}
