namespace TaskPane.Infrastructure.Paths;

/// <summary>
/// Ensures LocalAppData product folders exist and are writable without admin rights.
/// Does not move the live database onto Documents or network paths.
/// </summary>
public static class LocalDataGuard
{
    public sealed record ProbeResult(bool Ok, string DataDirectory, string? Error);

    public static ProbeResult EnsureWritableDataRoot(string? overrideRoot = null)
    {
        try
        {
            var data = AppPaths.GetDataDirectory(overrideRoot);
            AppPaths.GetLogDirectory(overrideRoot);
            AppPaths.GetLocalBackupsDirectory(overrideRoot);
            AppPaths.GetLocalExportsDirectory(overrideRoot);

            var probe = Path.Combine(data, ".write-probe");
            File.WriteAllText(probe, DateTimeOffset.UtcNow.ToString("O"));
            File.Delete(probe);
            return new ProbeResult(true, data, null);
        }
        catch (Exception ex)
        {
            return new ProbeResult(false, overrideRoot ?? string.Empty, ex.Message);
        }
    }
}
