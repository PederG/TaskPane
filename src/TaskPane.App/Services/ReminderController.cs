using Microsoft.Extensions.Logging;
using TaskPane.Application.Services;
using TaskPane.Core.Enums;
using TaskPane.Core.Ids;
using TaskPane.Core.Interfaces;
using TaskPane.Core.Reminders;

namespace TaskPane.App.Services;

/// <summary>Polls due reminders while the tray app is running and routes toast actions.</summary>
public sealed class ReminderController : IDisposable
{
    private readonly ReminderService _reminders;
    private readonly RecurrenceService _recurrence;
    private readonly INotificationPresenter _notifications;
    private readonly ILogger<ReminderController> _logger;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public event Action? RemindersChanged;
    public event Action<TaskId>? OpenTaskRequested;

    public ReminderController(
        ReminderService reminders,
        RecurrenceService recurrence,
        INotificationPresenter notifications,
        ILogger<ReminderController> logger)
    {
        _reminders = reminders;
        _recurrence = recurrence;
        _notifications = notifications;
        _logger = logger;
        _notifications.Activated += OnNotificationActivated;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = Task.Run(() => RunAsync(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _recurrence.EvaluateAllAsync(cancellationToken).ConfigureAwait(false);
                var results = await _reminders.ProcessDueAsync(cancellationToken).ConfigureAwait(false);
                if (results.Count > 0)
                {
                    RemindersChanged?.Invoke();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reminder/recurrence poll failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private void OnNotificationActivated(object? sender, ReminderActivationEventArgs e)
    {
        TrayIconService.InvokeOnUi(() => _ = HandleActivationAsync(e));
    }

    private async Task HandleActivationAsync(ReminderActivationEventArgs e)
    {
        try
        {
            switch (e.Action)
            {
                case ReminderNotificationAction.OpenTask:
                    OpenTaskRequested?.Invoke(e.TaskId);
                    break;
                case ReminderNotificationAction.Snooze:
                    await _reminders.SnoozeAsync(e.ReminderId).ConfigureAwait(true);
                    RemindersChanged?.Invoke();
                    break;
                case ReminderNotificationAction.Dismiss:
                    await _reminders.DismissAsync(e.ReminderId).ConfigureAwait(true);
                    RemindersChanged?.Invoke();
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reminder activation {Action} failed for {ReminderId}", e.Action, e.ReminderId);
        }
    }

    public void Dispose()
    {
        _notifications.Activated -= OnNotificationActivated;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
