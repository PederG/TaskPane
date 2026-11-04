using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaskPane.App.Services;
using TaskPane.App.ViewModels;
using TaskPane.App.Windows;
using TaskPane.Application.DependencyInjection;
using TaskPane.Application.Services;
using TaskPane.Core.Entities;
using TaskPane.Core.Interfaces;
using TaskPane.Infrastructure.DependencyInjection;
using TaskPane.Infrastructure.Logging;
using TaskPane.Infrastructure.Migrations;
using TaskPane.Infrastructure.Paths;
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
    private ICrashMarkerStore? _crashMarker;
    private UiPresentationController? _presentation;
    private PrivacyFileLoggerProvider? _fileLogger;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Temporary theme until persisted app settings load (Phase 7).
        ApplicationThemeManager.Apply(
            ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark
                ? ApplicationTheme.Dark
                : ApplicationTheme.Light,
            WindowBackdropType.Mica,
            updateAccent: true);

        _singleInstance = new SingleInstanceGuard();
        if (!_singleInstance.IsPrimaryInstance)
        {
            await SingleInstanceGuard.SignalExistingInstanceAsync().ConfigureAwait(true);
            Shutdown();
            return;
        }

        var dataProbe = LocalDataGuard.EnsureWritableDataRoot();
        if (!dataProbe.Ok)
        {
            System.Windows.MessageBox.Show(
                $"TaskPane cannot write to its local data folder.\n\n{dataProbe.Error}\n\nNo admin rights are required; check folder permissions for this Windows profile.",
                "TaskPane",
                System.Windows.MessageBoxButton.OK,
                MessageBoxImage.Error);
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
                _fileLogger = new PrivacyFileLoggerProvider();
                logging.AddProvider(_fileLogger);
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .ConfigureServices(services =>
            {
                // Register Windows autostart before infrastructure so TryAdd keeps this implementation.
                services.AddSingleton<IAutostartService, WindowsAutostartService>();
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
                services.AddSingleton(_ => new UiPresentationController(this));
            })
            .Build();

        _crashMarker = _host.Services.GetRequiredService<ICrashMarkerStore>();
        var unclean = _crashMarker.BeginSession();

        var migrator = _host.Services.GetRequiredService<SqliteMigrator>();
        await migrator.MigrateAsync().ConfigureAwait(true);

        _presentation = _host.Services.GetRequiredService<UiPresentationController>();
        var appSettings = await _host.Services.GetRequiredService<AppSettingsService>().GetAsync().ConfigureAwait(true);
        if (!appSettings.EnableFileLogging)
        {
            _fileLogger?.Dispose();
            _fileLogger = null;
        }

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
        managerVm.AppSettingsChanged += settings =>
        {
            _presentation?.Apply(settings);
            _tray?.SetStatusTooltip(settings.AutostartEnabled ? "Autostart on" : "Ready");
        };
        quickCaptureVm.Captured += () =>
        {
            _ = overlayVm.RefreshCommand.ExecuteAsync(null);
            _ = manager.InitializeAsync();
        };

        _tray = _host.Services.GetRequiredService<TrayIconService>();
        _tray.Initialize();
        _tray.SetStatusTooltip(unclean ? "Recovered after interruption" : "Ready");
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
        _presentation.Apply(appSettings);
        _modeController.ShowOverlay();

        if (unclean && appSettings.ShowCrashRecoveryNotice)
        {
            overlayVm.StatusMessage =
                "Recovered after an unexpected exit. Open timers were reconciled without charging offline time.";
        }

        _timeTrackingController = _host.Services.GetRequiredService<TimeTrackingController>();
        _timeTrackingController.SessionsChanged += () => TrayIconService.InvokeOnUi(() =>
        {
            _ = overlayVm.RefreshCommand.ExecuteAsync(null);
            _ = manager.InitializeAsync();
            _tray?.SetStatusTooltip("Timer updated");
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

        _crashMarker?.EndSessionCleanly();
        _backupController?.Dispose();
        _reminderController?.Dispose();
        _timeTrackingController?.Dispose();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _fileLogger?.Dispose();
        _singleInstance?.Dispose();
        _host?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _crashMarker?.EndSessionCleanly();
        _backupController?.Dispose();
        _reminderController?.Dispose();
        _timeTrackingController?.Dispose();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _fileLogger?.Dispose();
        _singleInstance?.Dispose();
        _host?.Dispose();
        base.OnExit(e);
    }
}
