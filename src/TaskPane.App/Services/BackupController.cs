using Microsoft.Extensions.Logging;
using TaskPane.Application.Services;
using TaskPane.Core.Entities;

namespace TaskPane.App.Services;

/// <summary>
/// Non-blocking periodic backup loop. Failures surface via status; they never stall the UI.
/// </summary>
public sealed class BackupController : IDisposable
{
    private readonly BackupService _backups;
    private readonly ILogger<BackupController> _logger;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public BackupController(BackupService backups, ILogger<BackupController> logger)
    {
        _backups = backups;
        _logger = logger;
    }

    public event Action? StatusChanged;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        // Kick an initial backup attempt so Documents reachability is probed early.
        try
        {
            await _backups.CreateBackupAsync("startup", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Startup backup attempt failed");
        }

        StatusChanged?.Invoke();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = RunLoopAsync(_cts.Token);
    }

    public async Task RunNowAsync(CancellationToken cancellationToken = default)
    {
        await _backups.CreateBackupAsync("manual", cancellationToken).ConfigureAwait(false);
        StatusChanged?.Invoke();
    }

    public async Task ShutdownAsync()
    {
        try
        {
            var settings = await _backups.GetSettingsAsync().ConfigureAwait(false);
            if (settings.BackupOnShutdown)
            {
                await _backups.CreateBackupAsync("shutdown").ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Shutdown backup failed");
        }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            BackupSettings settings;
            try
            {
                settings = await _backups.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                settings = new BackupSettings();
            }

            var delayMinutes = settings.IntervalMinutes <= 0 ? 30 : settings.IntervalMinutes;
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(delayMinutes), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (settings.IntervalMinutes <= 0)
            {
                continue;
            }

            try
            {
                await _backups.CreateBackupAsync("scheduled", cancellationToken).ConfigureAwait(false);
                StatusChanged?.Invoke();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Scheduled backup failed");
                StatusChanged?.Invoke();
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
