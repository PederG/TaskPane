using TaskPane.Core.Interfaces;

namespace TaskPane.Infrastructure.Paths;

public sealed class CrashMarkerStore : ICrashMarkerStore
{
    public const string MarkerFileName = "session.running";

    private readonly string _markerPath;
    private bool _hasUncleanMarker;

    public CrashMarkerStore(string? dataDirectory = null)
    {
        var root = dataDirectory ?? AppPaths.GetDataDirectory();
        Directory.CreateDirectory(root);
        _markerPath = Path.Combine(root, MarkerFileName);
    }

    public bool HasUncleanMarker => _hasUncleanMarker;

    public bool BeginSession()
    {
        _hasUncleanMarker = File.Exists(_markerPath);
        var payload = DateTimeOffset.UtcNow.ToString("O");
        File.WriteAllText(_markerPath, payload);
        return _hasUncleanMarker;
    }

    public void EndSessionCleanly()
    {
        try
        {
            if (File.Exists(_markerPath))
            {
                File.Delete(_markerPath);
            }

            _hasUncleanMarker = false;
        }
        catch
        {
            // Best effort — next launch may still show recovery.
        }
    }
}
