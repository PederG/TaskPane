using System.Windows;
using TaskPane.Application.Services;
using TaskPane.Core.Enums;
using TaskPane.Core.Interfaces;

namespace TaskPane.App.Services;

/// <summary>Wires workstation events and recovery prompts to <see cref="TimeTrackingService"/>.</summary>
public sealed class TimeTrackingController : IDisposable
{
    private readonly TimeTrackingService _timeTracking;
    private readonly IWorkstationActivityMonitor _monitor;
    private readonly WindowsWorkstationActivityMonitor? _windowsMonitor;
    private bool _started;

    public event Action? SessionsChanged;

    public Func<string, MessageBoxResult>? Confirm { get; set; }

    public TimeTrackingController(
        TimeTrackingService timeTracking,
        IWorkstationActivityMonitor monitor)
    {
        _timeTracking = timeTracking;
        _monitor = monitor;
        _windowsMonitor = monitor as WindowsWorkstationActivityMonitor;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        var settings = await _timeTracking.GetSettingsAsync(cancellationToken).ConfigureAwait(true);
        if (_windowsMonitor is not null)
        {
            _windowsMonitor.IdleThreshold = TimeSpan.FromMinutes(settings.IdleThresholdMinutes);
            _windowsMonitor.IdleThresholdExceeded += OnIdleThresholdExceeded;
        }

        _monitor.SessionLocked += OnLocked;
        _monitor.SessionUnlocked += OnUnlocked;
        _monitor.SystemSuspended += OnSuspended;
        _monitor.SystemResumed += OnResumed;
        _monitor.Start();

        await RecoverIfNeededAsync(cancellationToken).ConfigureAwait(true);
        await OfferPendingResumeAsync(cancellationToken).ConfigureAwait(true);
    }

    public async Task RefreshIdleThresholdAsync(CancellationToken cancellationToken = default)
    {
        if (_windowsMonitor is null)
        {
            return;
        }

        var settings = await _timeTracking.GetSettingsAsync(cancellationToken).ConfigureAwait(true);
        _windowsMonitor.IdleThreshold = TimeSpan.FromMinutes(settings.IdleThresholdMinutes);
    }

    private async Task RecoverIfNeededAsync(CancellationToken cancellationToken)
    {
        var open = await _timeTracking.GetActiveSessionAsync(cancellationToken).ConfigureAwait(true);
        if (open is null)
        {
            return;
        }

        var choice = Ask(
            "An open time session was found after restart. Discard it (no overnight hours), or close it at the last known update time?",
            "Discard",
            "Close at last update");

        if (choice == MessageBoxResult.Yes)
        {
            await _timeTracking.RecoverOpenSessionAsync(SessionRecoveryChoice.Discard, cancellationToken: cancellationToken)
                .ConfigureAwait(true);
        }
        else
        {
            await _timeTracking.RecoverOpenSessionAsync(
                    SessionRecoveryChoice.CloseAtKnownTime,
                    open.UpdatedUtc,
                    cancellationToken)
                .ConfigureAwait(true);
        }

        RaiseChanged();
    }

    private async Task OfferPendingResumeAsync(CancellationToken cancellationToken)
    {
        var settings = await _timeTracking.GetSettingsAsync(cancellationToken).ConfigureAwait(true);
        if (settings.PendingResumeTaskId is null)
        {
            return;
        }

        var reason = settings.PendingResumeReason ?? "pause";
        var resume = Ask(
            $"Timer was paused ({reason}). Resume tracking?",
            "Resume",
            "Not now");

        if (resume == MessageBoxResult.Yes)
        {
            await _timeTracking.ResumePendingAsync(cancellationToken).ConfigureAwait(true);
        }
        else
        {
            await _timeTracking.ClearPendingResumeOfferAsync(cancellationToken).ConfigureAwait(true);
        }

        RaiseChanged();
    }

    private void OnLocked(object? sender, EventArgs e)
        => _ = HandlePauseAsync(SessionEndReason.Locked);

    private void OnSuspended(object? sender, EventArgs e)
        => _ = HandlePauseAsync(SessionEndReason.Suspended);

    private void OnUnlocked(object? sender, EventArgs e)
        => _ = OfferPendingResumeAsync(CancellationToken.None);

    private void OnResumed(object? sender, EventArgs e)
        => _ = OfferPendingResumeAsync(CancellationToken.None);

    private async Task HandlePauseAsync(SessionEndReason reason)
    {
        await _timeTracking.EndActiveIfAnyAsync(reason, rememberForResume: true).ConfigureAwait(true);
        RaiseChanged();
    }

    private void OnIdleThresholdExceeded(object? sender, TimeSpan idle)
        => _ = HandleIdleAsync(idle);

    private async Task HandleIdleAsync(TimeSpan idle)
    {
        var open = await _timeTracking.GetActiveSessionAsync().ConfigureAwait(true);
        if (open is null)
        {
            return;
        }

        var keep = Ask(
            $"Idle for {(int)idle.TotalMinutes} minute(s). Keep tracked time up to idle onset and resume, or discard the idle interval and stop?",
            "Keep & resume",
            "Discard idle");

        var choice = keep == MessageBoxResult.Yes
            ? IdleResolutionChoice.KeepAndResume
            : IdleResolutionChoice.DiscardIdle;

        await _timeTracking.HandleIdleAsync(idle, choice).ConfigureAwait(true);
        RaiseChanged();
    }

    private MessageBoxResult Ask(string message, string yesText, string noText)
    {
        if (Confirm is not null)
        {
            return Confirm(message);
        }

        // WPF MessageBox uses Yes/No; button labels are fixed — message states the meaning.
        return MessageBox.Show(
            $"{message}\n\nYes = {yesText}\nNo = {noText}",
            "TaskPane time tracking",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
    }

    private void RaiseChanged() => SessionsChanged?.Invoke();

    public void Dispose()
    {
        if (!_started)
        {
            return;
        }

        _started = false;
        _monitor.SessionLocked -= OnLocked;
        _monitor.SessionUnlocked -= OnUnlocked;
        _monitor.SystemSuspended -= OnSuspended;
        _monitor.SystemResumed -= OnResumed;
        if (_windowsMonitor is not null)
        {
            _windowsMonitor.IdleThresholdExceeded -= OnIdleThresholdExceeded;
        }

        _monitor.Dispose();
    }
}
