using TaskPane.Core.Enums;
using TaskPane.Core.Ids;

namespace TaskPane.Core.Interfaces;

public sealed class ReminderNotification
{
    public required ReminderId ReminderId { get; init; }
    public required TaskId TaskId { get; init; }

    /// <summary>Task title only — never include notes body.</summary>
    public required string Title { get; init; }

    public required string DeliveryKey { get; init; }
    public DateTimeOffset DueUtc { get; init; }
}

public interface INotificationPresenter
{
    /// <summary>
    /// Attempts to show a discreet reminder. Implementations must not include task notes.
    /// </summary>
    Task<NotificationDeliveryStatus> ShowAsync(
        ReminderNotification notification,
        CancellationToken cancellationToken = default);
}
