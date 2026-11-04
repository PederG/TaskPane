using TaskPane.Core.Enums;

namespace TaskPane.Core.Entities;

/// <summary>General application preferences for shipping polish (autostart, theme, text scale, logging).</summary>
public sealed class AppSettings
{
    public const string SettingsKey = "app.settings";

    /// <summary>Opt-in per-user autostart via the current Windows profile Run key. Default off.</summary>
    public bool AutostartEnabled { get; set; }

    public ThemePreference Theme { get; set; } = ThemePreference.System;

    /// <summary>UI text/layout scale percent. Clamped to 100–150.</summary>
    public int UiTextScalePercent { get; set; } = 100;

    /// <summary>Write rolling logs under LocalAppData. Task titles/notes are never written by default.</summary>
    public bool EnableFileLogging { get; set; } = true;

    /// <summary>When true after an unclean exit, surface a recovery status message at startup.</summary>
    public bool ShowCrashRecoveryNotice { get; set; } = true;

    /// <summary>
    /// Product wordmark shown in overlay/manager chrome.
    /// Defaults to <see cref="Branding.AppBranding.DefaultAppDisplayName"/> (TaskPane in public builds).
    /// Workplace installs may set this in local settings or bake a build-time default via MSBuild
    /// <c>AppDisplayName</c> — do not commit private workplace names as the public default.
    /// </summary>
    public string AppDisplayName { get; set; } = Branding.AppBranding.DefaultAppDisplayName;
}
