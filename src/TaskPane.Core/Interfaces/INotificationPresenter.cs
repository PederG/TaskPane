using TaskPane.Core.Enums;
using TaskPane.Core.Ids;
using TaskPane.Core.Reminders;

namespace TaskPane.Core.Interfaces;

public sealed class ReminderNotification
{
    public required ReminderId ReminderId { get; init; }
    public required TaskId TaskId { get; init; }

    /// <summary>Task title only — never include notes body.</summary>
    public required string Title { get; init; }

    /// <summary>Product wordmark from <c>AppDisplayName</c> (public default TaskPane).</summary>
    public required string AppDisplayName { get; init; }

    public required string DeliveryKey { get; init; }
    public DateTimeOffset DueUtc { get; init; }

    /// <summary>Configured snooze length used when the user activates Snooze.</summary>
    public int SnoozeMinutes { get; init; } = 15;
}

public interface INotificationPresenter
{
    /// <summary>Raised when the user activates Open / Snooze / Dismiss on a reminder surface.</summary>
    event EventHandler<ReminderActivationEventArgs>? Activated;

    /// <summary>
    /// Attempts to show a discreet reminder. Implementations must not include task notes.
    /// </summary>
    Task<NotificationDeliveryStatus> ShowAsync(
        ReminderNotification notification,
        CancellationToken cancellationToken = default);
}
