using TaskPane.Core.Ids;

namespace TaskPane.Application.Models;

public sealed class TimeTotals
{
    public required TaskId TaskId { get; init; }
    public TimeSpan Direct { get; init; }
    public TimeSpan RolledUp { get; init; }
}

public sealed class TimeReportRow
{
    public required TaskId TaskId { get; init; }
    public required string Title { get; init; }
    public TimeSpan Direct { get; init; }
}

public sealed class TimeReport
{
    public required string Label { get; init; }
    public DateTimeOffset RangeStartLocal { get; init; }
    public DateTimeOffset RangeEndLocal { get; init; }
    public TimeSpan Total { get; init; }
    public IReadOnlyList<TimeReportRow> Rows { get; init; } = [];
}
