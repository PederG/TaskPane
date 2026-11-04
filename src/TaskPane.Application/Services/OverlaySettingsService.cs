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
        placement.Opacity = Math.Clamp(placement.Opacity, 0.45, 1.0);
        return _settings.SaveOverlayPlacementAsync(placement, cancellationToken);
    }
}
