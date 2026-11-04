using TaskPane.Core.Entities;

namespace TaskPane.Core.Interfaces;

public interface ISettingsRepository
{
    Task<OverlayPlacement> GetOverlayPlacementAsync(CancellationToken cancellationToken = default);

    Task SaveOverlayPlacementAsync(OverlayPlacement placement, CancellationToken cancellationToken = default);

    Task<string?> GetRawAsync(string key, CancellationToken cancellationToken = default);

    Task SetRawAsync(string key, string jsonValue, CancellationToken cancellationToken = default);
}
