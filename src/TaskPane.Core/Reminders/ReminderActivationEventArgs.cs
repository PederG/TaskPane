using TaskPane.Core.Enums;
using TaskPane.Core.Ids;

namespace TaskPane.Core.Reminders;

public sealed class ReminderActivationEventArgs : EventArgs
{
    public required ReminderId ReminderId { get; init; }
    public required TaskId TaskId { get; init; }
    public required ReminderNotificationAction Action { get; init; }
    public string? DeliveryKey { get; init; }
}
