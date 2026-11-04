using CommunityToolkit.Mvvm.ComponentModel;
using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using TaskPane.Core.Ids;
using TaskPane.Core.Time;

namespace TaskPane.App.ViewModels;

public partial class SessionRowViewModel : ObservableObject
{
    public SessionRowViewModel(TimeSession session)
    {
        Id = session.Id;
        TaskId = session.TaskId;
        StartedUtc = session.StartedUtc;
        EndedUtc = session.EndedUtc;
        EndReason = session.EndReason;
        IsDiscarded = session.IsDiscarded;
        IsOpen = session.IsOpen;
        var duration = session.Duration ?? (session.IsOpen ? DateTimeOffset.UtcNow - session.StartedUtc : TimeSpan.Zero);
        DurationText = session.IsDiscarded ? "discarded" : SessionDuration.FormatCompact(duration);
        Display = $"{StartedUtc:g} → {(EndedUtc?.ToString("g") ?? "open")} · {DurationText}"
                  + (EndReason is { } r ? $" · {r}" : string.Empty)
                  + (IsDiscarded ? " · discarded" : string.Empty);
    }

    public SessionId Id { get; }
    public TaskId TaskId { get; }
    public DateTimeOffset StartedUtc { get; }
    public DateTimeOffset? EndedUtc { get; }
    public SessionEndReason? EndReason { get; }
    public bool IsDiscarded { get; }
    public bool IsOpen { get; }
    public string DurationText { get; }
    public string Display { get; }
}
