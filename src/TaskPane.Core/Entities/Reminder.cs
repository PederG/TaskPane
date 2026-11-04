using TaskPane.Core.Enums;
using TaskPane.Core.Ids;

namespace TaskPane.Core.Entities;

public sealed class Reminder
{
    public required ReminderId Id { get; init; }
    public required TaskId TaskId { get; set; }
    public DateTimeOffset DueUtc { get; set; }
    public DateTimeOffset? DeliveredUtc { get; set; }
    public DateTimeOffset? DismissedUtc { get; set; }
    public DateTimeOffset? SnoozedUntilUtc { get; set; }
    public ReminderKind Kind { get; set; } = ReminderKind.Absolute;

    /// <summary>Dedup key across process restarts: reminder id + due instant.</summary>
    public required string DeliveryKey { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public bool IsDismissed => DismissedUtc is not null;
    public bool IsDelivered => DeliveredUtc is not null;
}
