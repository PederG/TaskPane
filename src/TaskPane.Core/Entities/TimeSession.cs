using TaskPane.Core.Enums;
using TaskPane.Core.Ids;

namespace TaskPane.Core.Entities;

public sealed class TimeSession
{
    public required SessionId Id { get; init; }
    public required TaskId TaskId { get; set; }
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset? EndedUtc { get; set; }
    public SessionEndReason? EndReason { get; set; }
    public bool IsDiscarded { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }

    /// <summary>1 while open; null when closed. Enforces single-open via unique index.</summary>
    public int? OpenSlot { get; set; }

    public bool IsOpen => EndedUtc is null && OpenSlot == 1 && !IsDiscarded;

    public TimeSpan? Duration
    {
        get
        {
            if (IsDiscarded || EndedUtc is null)
            {
                return null;
            }

            var span = EndedUtc.Value - StartedUtc;
            return span < TimeSpan.Zero ? TimeSpan.Zero : span;
        }
    }
}
