using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaskPane.App.Services;
using TaskPane.App.ViewModels;
using TaskPane.App.Windows;
using TaskPane.Application.DependencyInjection;
using TaskPane.Core.Entities;
using TaskPane.Core.Interfaces;
using TaskPane.Infrastructure.DependencyInjection;
using TaskPane.Infrastructure.Migrations;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace TaskPane.App;

public partial class App : System.Windows.Application
{
    private IHost? _host;
    private SingleInstanceGuard? _singleInstance;
    private TrayIconService? _tray;
    private WindowModeController? _modeController;
    private GlobalHotkeyService? _hotkeys;
    private QuickCaptureWindow? _quickCapture;
    private TimeTrackingController? _timeTrackingController;
    private ReminderController? _reminderController;
    private BackupController? _backupController;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.Mica, updateAccent: true);

        _singleInstance = new SingleInstanceGuard();
        if (!_singleInstance.IsPrimaryInstance)
        {
            await SingleInstanceGuard.SignalExistingInstanceAsync().ConfigureAwait(true);
            Shutdown();
            return;
        }

        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddSimpleConsole(options =>
                {
                    options.SingleLine = true;
                    options.TimestampFormat = "HH:mm:ss ";
                });
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .ConfigureServices(services =>
            {
                services.AddTaskPaneInfrastructure();
                services.AddTaskPaneApplication();
                services.AddSingleton<IWorkstationActivityMonitor, WindowsWorkstationActivityMonitor>();
                services.AddSingleton<INotificationPresenter, WindowsNotificationPresenter>();
                services.AddSingleton<TimeTrackingController>();
                services.AddSingleton<ReminderController>();
                services.AddSingleton<BackupController>();
                services.AddSingleton<OverlayViewModel>();
                services.AddSingleton<ManagerViewModel>();
                services.AddSingleton<QuickCaptureViewModel>();
                services.AddSingleton<OverlayWindow>();
                services.AddSingleton<ManagerWindow>();
                services.AddSingleton<QuickCaptureWindow>();
                services.AddSingleton<WindowModeController>();
                services.AddSingleton<TrayIconService>();
                services.AddSingleton<GlobalHotkeyService>();
            })
            .Build();

        var migrator = _host.Services.GetRequiredService<SqliteMigrator>();
        await migrator.MigrateAsync().ConfigureAwait(true);

        _modeController = _host.Services.GetRequiredService<WindowModeController>();
        var overlay = _host.Services.GetRequiredService<OverlayWindow>();
        var manager = _host.Services.GetRequiredService<ManagerWindow>();
        var overlayVm = _host.Services.GetRequiredService<OverlayViewModel>();
        var managerVm = _host.Services.GetRequiredService<ManagerViewModel>();
        _quickCapture = _host.Services.GetRequiredService<QuickCaptureWindow>();
        var quickCaptureVm = _host.Services.GetRequiredService<QuickCaptureViewModel>();

        _modeController.Attach(overlay, manager);
        overlayVm.OpenManagerRequested += () =>
        {
            _ = manager.InitializeAsync();
            _modeController.ShowManager();
        };
        overlayVm.QuitRequested += QuitApplication;
        managerVm.ReturnToOverlayRequested += () => _modeController.ShowOverlay();
        managerVm.HotkeyChanged += setting => _ = ApplyHotkeyAsync(setting);
        quickCaptureVm.Captured += () =>
        {
            _ = overlayVm.RefreshCommand.ExecuteAsync(null);
            _ = manager.InitializeAsync();
        };

        _tray = _host.Services.GetRequiredService<TrayIconService>();
        _tray.Initialize();
        _tray.ShowOverlayRequested += () => TrayIconService.InvokeOnUi(() => _modeController.ShowOverlay());
        _tray.OpenManagerRequested += () => TrayIconService.InvokeOnUi(() =>
        {
            _ = manager.InitializeAsync();
            _modeController.ShowManager();
        });
        _tray.ShowQuickCaptureRequested += () => TrayIconService.InvokeOnUi(ShowQuickCapture);
        _tray.QuitRequested += () => TrayIconService.InvokeOnUi(QuitApplication);

        _singleInstance.ActivationRequested += () => TrayIconService.InvokeOnUi(() =>
        {
            if (manager.IsVisible)
            {
                manager.Activate();
            }
            else
            {
                _modeController.ShowOverlay();
            }
        });
        _singleInstance.StartListening();

        await overlay.InitializeAsync().ConfigureAwait(true);
        _modeController.ShowOverlay();

        _timeTrackingController = _host.Services.GetRequiredService<TimeTrackingController>();
        _timeTrackingController.SessionsChanged += () => TrayIconService.InvokeOnUi(() =>
        {
            _ = overlayVm.RefreshCommand.ExecuteAsync(null);
            _ = manager.InitializeAsync();
        });
        managerVm.IdleThresholdChanged += () => _ = _timeTrackingController.RefreshIdleThresholdAsync();
        await _timeTrackingController.StartAsync().ConfigureAwait(true);

        _reminderController = _host.Services.GetRequiredService<ReminderController>();
        _reminderController.RemindersChanged += () => TrayIconService.InvokeOnUi(() =>
        {
            _ = overlayVm.RefreshCommand.ExecuteAsync(null);
            _ = manager.InitializeAsync();
        });
        await _reminderController.StartAsync().ConfigureAwait(true);

        _backupController = _host.Services.GetRequiredService<BackupController>();
        _backupController.StatusChanged += () => TrayIconService.InvokeOnUi(() =>
        {
            _ = managerVm.RefreshBackupStatusAsync();
        });
        await _backupController.StartAsync().ConfigureAwait(true);

        _hotkeys = _host.Services.GetRequiredService<GlobalHotkeyService>();
        await _hotkeys.InitializeAsync(overlay).ConfigureAwait(true);
        _hotkeys.HotkeyPressed += () => TrayIconService.InvokeOnUi(ShowQuickCapture);
        if (_hotkeys.LastError is { } hotkeyError)
        {
            overlayVm.StatusMessage = hotkeyError + " Use tray → Quick capture.";
        }
    }

    private async Task ApplyHotkeyAsync(HotkeySetting setting)
    {
        if (_hotkeys is null)
        {
            return;
        }

        try
        {
            await _hotkeys.ApplyAsync(setting).ConfigureAwait(true);
            var overlayVm = _host?.Services.GetRequiredService<OverlayViewModel>();
            if (overlayVm is not null)
            {
                overlayVm.StatusMessage = setting.Enabled
                    ? $"Hotkey active: {setting.DisplayLabel}"
                    : "Global hotkey disabled.";
            }
        }
        catch (Exception ex)
        {
            var overlayVm = _host?.Services.GetRequiredService<OverlayViewModel>();
            if (overlayVm is not null)
            {
                overlayVm.StatusMessage = ex.Message;
            }
        }
    }

    private void ShowQuickCapture()
    {
        _quickCapture?.ShowCapture();
    }

    private void QuitApplication()
    {
        try
        {
            _backupController?.ShutdownAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // best effort shutdown backup
        }

        _backupController?.Dispose();
        _reminderController?.Dispose();
        _timeTrackingController?.Dispose();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        _host?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _backupController?.Dispose();
        _reminderController?.Dispose();
        _timeTrackingController?.Dispose();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        _host?.Dispose();
        base.OnExit(e);
    }
}
