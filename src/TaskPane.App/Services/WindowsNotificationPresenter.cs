using Microsoft.Extensions.Logging;
using TaskPane.App.Windows;
using TaskPane.Core.Enums;
using TaskPane.Core.Ids;
using TaskPane.Core.Interfaces;
using TaskPane.Core.Reminders;

namespace TaskPane.App.Services;

/// <summary>
/// Windows 11 toast adapter with tray + in-app fallback for unpackaged WPF.
/// Never includes task notes in notification text.
/// </summary>
public sealed class WindowsNotificationPresenter : INotificationPresenter
{
    private readonly TrayIconService _tray;
    private readonly ILogger<WindowsNotificationPresenter> _logger;
    private ReminderNotification? _lastNotification;

    public WindowsNotificationPresenter(
        TrayIconService tray,
        ILogger<WindowsNotificationPresenter> logger)
    {
        _tray = tray;
        _logger = logger;
        _tray.BalloonTipClicked += OnBalloonTipClicked;
    }

    public event EventHandler<ReminderActivationEventArgs>? Activated;

    public Task<NotificationDeliveryStatus> ShowAsync(
        ReminderNotification notification,
        CancellationToken cancellationToken = default)
    {
        _lastNotification = notification;
        var brand = string.IsNullOrWhiteSpace(notification.AppDisplayName)
            ? "TaskPane"
            : notification.AppDisplayName.Trim();

        try
        {
            var shown = false;
            TrayIconService.InvokeOnUi(() =>
            {
                shown = WindowsToastDelivery.TryShow(
                    notification,
                    OnToastArguments,
                    _logger);
            });

            if (shown)
            {
                return Task.FromResult(NotificationDeliveryStatus.Delivered);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Toast presenter failed; falling back");
        }

        try
        {
            TrayIconService.InvokeOnUi(() =>
            {
                _tray.ShowBalloon(
                    $"{brand} reminder",
                    notification.Title,
                    System.Windows.Forms.ToolTipIcon.Info);

                var window = new ReminderActionWindow(notification, Raise);
                window.Show();
            });

            return Task.FromResult(NotificationDeliveryStatus.FallbackInApp);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "In-app reminder fallback unavailable");
            return Task.FromResult(NotificationDeliveryStatus.Unavailable);
        }
    }

    private void OnToastArguments(string arguments)
    {
        if (!ReminderToastContent.TryParseArguments(arguments, out var action, out var reminderId, out var taskId))
        {
            return;
        }

        var mapped = action switch
        {
            "snooze" => ReminderNotificationAction.Snooze,
            "dismiss" => ReminderNotificationAction.Dismiss,
            _ => ReminderNotificationAction.OpenTask
        };

        Raise(mapped, new ReminderNotification
        {
            ReminderId = new ReminderId(reminderId),
            TaskId = new TaskId(taskId),
            Title = _lastNotification?.Title ?? string.Empty,
            AppDisplayName = _lastNotification?.AppDisplayName ?? "TaskPane",
            DeliveryKey = _lastNotification?.DeliveryKey ?? string.Empty,
            DueUtc = _lastNotification?.DueUtc ?? default,
            SnoozeMinutes = _lastNotification?.SnoozeMinutes ?? 15
        });
    }

    private void OnBalloonTipClicked()
    {
        if (_lastNotification is null)
        {
            return;
        }

        Raise(ReminderNotificationAction.OpenTask, _lastNotification);
    }

    private void Raise(ReminderNotificationAction action, ReminderNotification notification)
    {
        Activated?.Invoke(this, new ReminderActivationEventArgs
        {
            Action = action,
            ReminderId = notification.ReminderId,
            TaskId = notification.TaskId,
            DeliveryKey = notification.DeliveryKey
        });
    }
}
