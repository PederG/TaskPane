namespace TaskPane.Infrastructure.Paths;

/// <summary>
/// Resolves local storage under LocalApplicationData. Never uses Documents/network paths for the live DB.
/// </summary>
public static class AppPaths
{
    public const string ProductFolderName = "TaskPane";
    public const string DatabaseFileName = "tasks.db";
    public const string BackupsFolderName = "Backups";
    public const string ExportsFolderName = "Exports";

    public static string GetDataDirectory(string? overrideRoot = null)
    {
        var root = overrideRoot
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException("LocalApplicationData path is unavailable.");
        }

        var path = Path.Combine(root, ProductFolderName);
        Directory.CreateDirectory(path);
        return path;
    }

    public static string GetDatabasePath(string? overrideRoot = null)
        => Path.Combine(GetDataDirectory(overrideRoot), DatabaseFileName);

    public static string GetLogDirectory(string? overrideRoot = null)
    {
        var path = Path.Combine(GetDataDirectory(overrideRoot), "logs");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Local fallback backup directory (always on the same volume as the live DB).</summary>
    public static string GetLocalBackupsDirectory(string? overrideRoot = null)
    {
        var path = Path.Combine(GetDataDirectory(overrideRoot), BackupsFolderName);
        Directory.CreateDirectory(path);
        return path;
    }

    public static string GetLocalExportsDirectory(string? overrideRoot = null)
    {
        var path = Path.Combine(GetDataDirectory(overrideRoot), ExportsFolderName);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Documents product root when the known folder is available.
    /// Never used for the live database.
    /// </summary>
    public static string GetDocumentsProductDirectory(string documentsRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentsRoot);
        var path = Path.Combine(documentsRoot, ProductFolderName);
        Directory.CreateDirectory(path);
        return path;
    }

    public static string GetDocumentsBackupsDirectory(string documentsRoot)
    {
        var path = Path.Combine(GetDocumentsProductDirectory(documentsRoot), BackupsFolderName);
        Directory.CreateDirectory(path);
        return path;
    }

    public static string GetDocumentsExportsDirectory(string documentsRoot)
    {
        var path = Path.Combine(GetDocumentsProductDirectory(documentsRoot), ExportsFolderName);
        Directory.CreateDirectory(path);
        return path;
    }

    public static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "export";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "export" : cleaned;
    }
}
