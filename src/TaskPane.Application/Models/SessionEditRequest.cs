using TaskPane.Core.Ids;

namespace TaskPane.Application.Models;

public sealed class SessionEditRequest
{
    public required SessionId Id { get; init; }
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset EndedUtc { get; init; }
    public bool IsDiscarded { get; init; }
}

public sealed class ManualSessionRequest
{
    public required TaskId TaskId { get; init; }
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset EndedUtc { get; init; }
}
