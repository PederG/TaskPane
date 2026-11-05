using System.Windows;
using System.Windows.Media;
using TaskPane.App.Windows;
using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace TaskPane.App.Services;

/// <summary>
/// Applies theme preference and UI text scale without rewriting Fluent XAML trees.
/// Theme uses WPF-UI ApplicationThemeManager so the redesign stays authoritative.
/// Global backdrop is <see cref="WindowBackdropType.None"/> so Apply never paints DWM Mica
/// onto the compact overlay layered HWND; manager Mica is restored separately.
/// </summary>
public sealed class UiPresentationController
{
    private readonly System.Windows.Application _application;

    public UiPresentationController(System.Windows.Application application)
    {
        _application = application;
    }

    public void Apply(AppSettings settings)
    {
        ApplyTheme(settings.Theme);
        ApplyTextScale(settings.UiTextScalePercent);
    }

    public void ApplyTheme(ThemePreference preference)
    {
        var theme = preference switch
        {
            ThemePreference.Light => ApplicationTheme.Light,
            ThemePreference.Dark => ApplicationTheme.Dark,
            _ => ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark
                ? ApplicationTheme.Dark
                : ApplicationTheme.Light
        };

        // None at the ApplicationThemeManager level: UpdateBackground(MainWindow, …)
        // must not apply system backdrop to a per-pixel-alpha OverlayWindow.
        ApplicationThemeManager.Apply(theme, WindowBackdropType.None, updateAccent: true);
        EnsureManagerMica();
    }

    /// <summary>
    /// Re-assert Mica on the expanded manager after a global Apply(None) or theme swap.
    /// Manager owns native backdrop via FluentWindow; compact overlay stays isolated.
    /// </summary>
    public void EnsureManagerMica()
    {
        foreach (Window window in _application.Windows)
        {
            if (window is not ManagerWindow manager)
            {
                continue;
            }

            manager.WindowBackdropType = WindowBackdropType.Mica;
            if (!manager.IsLoaded)
            {
                continue;
            }

            // Setting the DP to the same value may not re-run OnBackdropTypeChanged after
            // ApplicationThemeManager.Apply(None) cleared DWM backdrop on MainWindow.
            _ = WindowBackdrop.ApplyBackdrop(manager, WindowBackdropType.Mica);
        }
    }

    public void ApplyTextScale(int percent)
    {
        var scale = Math.Clamp(percent, 100, 150) / 100.0;
        foreach (Window window in _application.Windows)
        {
            ApplyScaleToWindow(window, scale);
        }
    }

    public static void ApplyScaleToWindow(Window window, double scale)
    {
        if (window.Content is FrameworkElement root)
        {
            root.LayoutTransform = Math.Abs(scale - 1.0) < 0.001
                ? Transform.Identity
                : new ScaleTransform(scale, scale);
        }
    }
}
