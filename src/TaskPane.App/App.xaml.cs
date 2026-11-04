using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaskPane.App.Services;
using TaskPane.App.ViewModels;
using TaskPane.App.Windows;
using TaskPane.Application.DependencyInjection;
using TaskPane.Infrastructure.DependencyInjection;
using TaskPane.Infrastructure.Migrations;

namespace TaskPane.App;

public partial class App : System.Windows.Application
{
    private IHost? _host;
    private SingleInstanceGuard? _singleInstance;
    private TrayIconService? _tray;
    private WindowModeController? _modeController;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
                // Avoid logging task content: application code logs IDs only.
            })
            .ConfigureServices(services =>
            {
                services.AddTaskPaneInfrastructure();
                services.AddTaskPaneApplication();
                services.AddSingleton<OverlayViewModel>();
                services.AddSingleton<ManagerViewModel>();
                services.AddSingleton<OverlayWindow>();
                services.AddSingleton<ManagerWindow>();
                services.AddSingleton<WindowModeController>();
                services.AddSingleton<TrayIconService>();
            })
            .Build();

        var migrator = _host.Services.GetRequiredService<SqliteMigrator>();
        await migrator.MigrateAsync().ConfigureAwait(true);

        _modeController = _host.Services.GetRequiredService<WindowModeController>();
        var overlay = _host.Services.GetRequiredService<OverlayWindow>();
        var manager = _host.Services.GetRequiredService<ManagerWindow>();
        var overlayVm = _host.Services.GetRequiredService<OverlayViewModel>();
        var managerVm = _host.Services.GetRequiredService<ManagerViewModel>();

        _modeController.Attach(overlay, manager);
        overlayVm.OpenManagerRequested += () =>
        {
            _ = manager.InitializeAsync();
            _modeController.ShowManager();
        };
        overlayVm.QuitRequested += QuitApplication;
        managerVm.ReturnToOverlayRequested += () => _modeController.ShowOverlay();

        _tray = _host.Services.GetRequiredService<TrayIconService>();
        _tray.Initialize();
        _tray.ShowOverlayRequested += () => TrayIconService.InvokeOnUi(() => _modeController.ShowOverlay());
        _tray.OpenManagerRequested += () => TrayIconService.InvokeOnUi(() =>
        {
            _ = manager.InitializeAsync();
            _modeController.ShowManager();
        });
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
    }

    private void QuitApplication()
    {
        _tray?.Dispose();
        _singleInstance?.Dispose();
        _host?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _singleInstance?.Dispose();
        _host?.Dispose();
        base.OnExit(e);
    }
}
