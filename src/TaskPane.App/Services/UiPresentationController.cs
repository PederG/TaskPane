using System.Windows;
using System.Windows.Media;
using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace TaskPane.App.Services;

/// <summary>
/// Applies theme preference and UI text scale without rewriting Fluent XAML trees.
/// Theme uses WPF-UI ApplicationThemeManager so the redesign stays authoritative.
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

        ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, updateAccent: true);
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
