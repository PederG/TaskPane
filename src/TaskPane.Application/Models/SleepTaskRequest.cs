using TaskPane.Core.Enums;
using TaskPane.Core.Ids;

namespace TaskPane.Application.Models;

public sealed class SleepTaskRequest
{
    public required TaskId Id { get; init; }
    public required SleepPreset Preset { get; init; }
    public DateTimeOffset? CustomUntilUtc { get; init; }
}
