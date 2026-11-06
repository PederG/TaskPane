using TaskPane.Core.Entities;
using TaskPane.Core.Interfaces;

namespace TaskPane.Application.Services;

public sealed class OverlaySettingsService
{
    private readonly ISettingsRepository _settings;

    public OverlaySettingsService(ISettingsRepository settings)
    {
        _settings = settings;
    }

    public Task<OverlayPlacement> GetAsync(CancellationToken cancellationToken = default)
        => _settings.GetOverlayPlacementAsync(cancellationToken);

    public Task SaveAsync(OverlayPlacement placement, CancellationToken cancellationToken = default)
    {
        placement.Width = Math.Clamp(placement.Width, 280, 900);
        placement.Height = Math.Clamp(placement.Height, 220, 1200);
        placement.MaxHeight = Math.Clamp(placement.MaxHeight, 240, 1200);
        // Opacity was removed from OverlayPlacement; legacy "opacity" in stored JSON is
        // ignored on deserialize and omitted on the next save (no Settings-row delete).
        return _settings.SaveOverlayPlacementAsync(placement, cancellationToken);
    }
}
