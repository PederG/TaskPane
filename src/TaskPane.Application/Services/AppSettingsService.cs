using System.Text.Json;
using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using TaskPane.Core.Interfaces;

namespace TaskPane.Application.Services;

public sealed class AppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly ISettingsRepository _settings;
    private readonly IAutostartService _autostart;

    public AppSettingsService(ISettingsRepository settings, IAutostartService autostart)
    {
        _settings = settings;
        _autostart = autostart;
    }

    public async Task<AppSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        var raw = await _settings.GetRawAsync(AppSettings.SettingsKey, cancellationToken).ConfigureAwait(false);
        AppSettings settings;
        if (string.IsNullOrWhiteSpace(raw))
        {
            settings = new AppSettings();
        }
        else
        {
            try
            {
                settings = JsonSerializer.Deserialize<AppSettings>(raw, JsonOptions) ?? new AppSettings();
            }
            catch (JsonException)
            {
                settings = new AppSettings();
            }
        }

        Normalize(settings);

        // Prefer the live OS registration when the platform supports it.
        if (_autostart.IsSupported)
        {
            settings.AutostartEnabled = _autostart.IsEnabled();
        }

        return settings;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Normalize(settings);

        if (_autostart.IsSupported)
        {
            _autostart.SetEnabled(settings.AutostartEnabled);
            settings.AutostartEnabled = _autostart.IsEnabled();
        }
        else
        {
            // Persist the preference even when the host cannot register autostart (e.g. tests/Linux).
            settings.AutostartEnabled = settings.AutostartEnabled;
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await _settings.SetRawAsync(AppSettings.SettingsKey, json, cancellationToken).ConfigureAwait(false);
    }

    public static void Normalize(AppSettings settings)
    {
        settings.UiTextScalePercent = Math.Clamp(settings.UiTextScalePercent, 100, 150);
        if (!Enum.IsDefined(settings.Theme))
        {
            settings.Theme = ThemePreference.System;
        }
    }
}
