namespace TaskPane.Core.Interfaces;

/// <summary>
/// Performs a consistent SQLite online backup of the live database.
/// Never copy an open DB file byte-for-byte.
/// </summary>
public interface IDatabaseBackupRunner
{
    /// <summary>Creates a consistent snapshot of the live DB at <paramref name="destinationPath"/>.</summary>
    Task BackupLiveToAsync(string destinationPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores <paramref name="backupPath"/> into the live database using the online backup API.
    /// Caller must have already created a safety backup of the current live DB.
    /// </summary>
    Task RestoreBackupIntoLiveAsync(string backupPath, CancellationToken cancellationToken = default);

    /// <summary>Opens a backup file and verifies it is a readable SQLite database with schema_version.</summary>
    Task<bool> VerifyBackupAsync(string backupPath, CancellationToken cancellationToken = default);

    string LiveDatabasePath { get; }
}
