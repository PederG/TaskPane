namespace TaskPane.Infrastructure.Paths;

/// <summary>
/// Resolves local storage under LocalApplicationData. Never uses Documents/network paths for the live DB.
/// </summary>
public static class AppPaths
{
    public const string ProductFolderName = "TaskPane";
    public const string DatabaseFileName = "tasks.db";

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
}
