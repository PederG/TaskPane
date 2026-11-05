using TaskPane.App.Windows;
using TaskPane.Core.Entities;

namespace TaskPane.App.Services;

/// <summary>Coordinates overlay ↔ manager visibility and restores overlay geometry.</summary>
public sealed class WindowModeController
{
    private OverlayWindow? _overlay;
    private ManagerWindow? _manager;
    private OverlayPlacement? _savedPlacement;

    public void Attach(OverlayWindow overlay, ManagerWindow manager)
    {
        _overlay = overlay;
        _manager = manager;
        // ApplicationThemeManager.Apply / SystemThemeWatcher.ApplySystemTheme call
        // UpdateBackground(MainWindow, …). Keep MainWindow on the expanded manager so
        // default Mica from those paths never lands on the layered compact overlay.
        System.Windows.Application.Current.MainWindow = manager;
    }

    public void ShowOverlay()
    {
        if (_overlay is null || _manager is null)
        {
            return;
        }

        _manager.Hide();
        if (_savedPlacement is not null)
        {
            _overlay.ApplyPlacement(_savedPlacement);
        }

        _overlay.Show();
        _overlay.Activate();
        // Re-assert after Show(): WPF only assigns MainWindow when it was null, but be explicit.
        System.Windows.Application.Current.MainWindow = _manager;
    }

    public void ShowManager()
    {
        if (_overlay is null || _manager is null)
        {
            return;
        }

        _savedPlacement = _overlay.CapturePlacement();
        _overlay.Hide();
        _manager.Show();
        _manager.Activate();
        System.Windows.Application.Current.MainWindow = _manager;
    }
}
