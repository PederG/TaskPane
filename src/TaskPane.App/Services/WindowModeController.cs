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
    }
}
