using TaskPane.Core.Enums;
using TaskPane.Core.Interfaces;

namespace TaskPane.Infrastructure.Notifications;

/// <summary>Safe fallback used in tests and non-Windows hosts — records as in-app fallback.</summary>
public sealed class NullNotificationPresenter : INotificationPresenter
{
    public List<ReminderNotification> Delivered { get; } = [];

    public Task<NotificationDeliveryStatus> ShowAsync(
        ReminderNotification notification,
        CancellationToken cancellationToken = default)
    {
        Delivered.Add(notification);
        return Task.FromResult(NotificationDeliveryStatus.FallbackInApp);
    }
}
