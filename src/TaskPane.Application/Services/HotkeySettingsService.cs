using System.Text.Json;
using TaskPane.Core.Entities;
using TaskPane.Core.Interfaces;

namespace TaskPane.Application.Services;

public sealed class HotkeySettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly ISettingsRepository _settings;

    public HotkeySettingsService(ISettingsRepository settings)
    {
        _settings = settings;
    }

    public async Task<HotkeySetting> GetAsync(CancellationToken cancellationToken = default)
    {
        var raw = await _settings.GetRawAsync(HotkeySetting.SettingsKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new HotkeySetting();
        }

        try
        {
            return JsonSerializer.Deserialize<HotkeySetting>(raw, JsonOptions) ?? new HotkeySetting();
        }
        catch (JsonException)
        {
            return new HotkeySetting();
        }
    }

    public Task SaveAsync(HotkeySetting setting, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(setting, JsonOptions);
        return _settings.SetRawAsync(HotkeySetting.SettingsKey, json, cancellationToken);
    }
}
