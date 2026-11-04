using System.Windows.Forms;
using TaskPane.Core.Enums;
using TaskPane.Core.Interfaces;

namespace TaskPane.App.Services;

/// <summary>
/// Windows 11 notification adapter with safe tray-balloon fallback.
/// Never includes task notes in the notification body.
/// </summary>
public sealed class WindowsNotificationPresenter : INotificationPresenter
{
    private readonly TrayIconService _tray;

    public WindowsNotificationPresenter(TrayIconService tray)
    {
        _tray = tray;
    }

    public Task<NotificationDeliveryStatus> ShowAsync(
        ReminderNotification notification,
        CancellationToken cancellationToken = default)
    {
        try
        {
            TrayIconService.InvokeOnUi(() => _tray.ShowBalloon(
                "TaskPane reminder",
                notification.Title,
                ToolTipIcon.Info));
            return Task.FromResult(NotificationDeliveryStatus.Delivered);
        }
        catch
        {
            return Task.FromResult(NotificationDeliveryStatus.Unavailable);
        }
    }
}
