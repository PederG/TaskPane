namespace TaskPane.Core.Entities;

public sealed class BackupSettings
{
    public const string SettingsKey = "backup.settings";

    /// <summary>How many verified local backup files to retain.</summary>
    public int LocalRetentionCount { get; set; } = 10;

    /// <summary>How many verified Documents backup files to retain when reachable.</summary>
    public int DocumentsRetentionCount { get; set; } = 10;

    /// <summary>Automatic backup interval while the app runs. Zero disables automatic backups.</summary>
    public int IntervalMinutes { get; set; } = 30;

    /// <summary>Also attempt a backup when the process is shutting down.</summary>
    public bool BackupOnShutdown { get; set; } = true;
}
