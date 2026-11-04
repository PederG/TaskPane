using TaskPane.Core.Enums;
using TaskPane.Core.Ids;

namespace TaskPane.Core.Entities;

public sealed class BackupHistoryEntry
{
    public required BackupHistoryId Id { get; init; }
    public DateTimeOffset CreatedUtc { get; set; }
    public required string LocalPath { get; set; }
    public string? DocumentsPath { get; set; }
    public BackupOutcome Result { get; set; }
    public string? ErrorCode { get; set; }
    public string? Message { get; set; }
}
