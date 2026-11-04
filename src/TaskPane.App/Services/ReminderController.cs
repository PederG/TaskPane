using Microsoft.Extensions.Logging;
using TaskPane.Application.Services;

namespace TaskPane.App.Services;

/// <summary>Polls due reminders while the tray app is running.</summary>
public sealed class ReminderController : IDisposable
{
    private readonly ReminderService _reminders;
    private readonly RecurrenceService _recurrence;
    private readonly ILogger<ReminderController> _logger;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public event Action? RemindersChanged;

    public ReminderController(
        ReminderService reminders,
        RecurrenceService recurrence,
        ILogger<ReminderController> logger)
    {
        _reminders = reminders;
        _recurrence = recurrence;
        _logger = logger;
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

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
