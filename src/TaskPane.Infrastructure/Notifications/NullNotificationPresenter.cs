using TaskPane.Core.Enums;
using TaskPane.Core.Interfaces;
using TaskPane.Core.Reminders;

namespace TaskPane.Infrastructure.Notifications;

/// <summary>Safe fallback used in tests and non-Windows hosts — records as in-app fallback.</summary>
public sealed class NullNotificationPresenter : INotificationPresenter
{
    public event EventHandler<ReminderActivationEventArgs>? Activated;

    public List<ReminderNotification> Delivered { get; } = [];

    public Task<NotificationDeliveryStatus> ShowAsync(
        ReminderNotification notification,
        CancellationToken cancellationToken = default)
    {
        Delivered.Add(notification);
        return Task.FromResult(NotificationDeliveryStatus.FallbackInApp);
    }

    /// <summary>Test helper to simulate toast / fallback button activation.</summary>
    public void RaiseActivated(ReminderActivationEventArgs args)
        => Activated?.Invoke(this, args);
}
