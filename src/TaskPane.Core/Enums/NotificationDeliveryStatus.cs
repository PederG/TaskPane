namespace TaskPane.Core.Enums;

public enum NotificationDeliveryStatus
{
    Delivered = 0,
    DeferredQuietHours = 1,
    FallbackInApp = 2,
    Unavailable = 3,
    SkippedAlreadyDelivered = 4,
    SkippedDismissed = 5,
    SkippedNotDue = 6
}
