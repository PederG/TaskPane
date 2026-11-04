namespace TaskPane.Core.Entities;

/// <summary>Persisted overlay geometry and presentation for the current Windows profile.</summary>
public sealed class OverlayPlacement
{
    public double Left { get; set; } = 80;
    public double Top { get; set; } = 80;
    public double Width { get; set; } = 340;
    public double Height { get; set; } = 480;

    /// <summary>Ceiling for auto-fit overlay height. Window sizes to content up to this value.</summary>
    public double MaxHeight { get; set; } = 720;

    public double Opacity { get; set; } = 0.92;
    public bool Topmost { get; set; } = true;
    public bool PositionLocked { get; set; }
    public string? MonitorDeviceName { get; set; }
}
