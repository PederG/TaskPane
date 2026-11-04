using Microsoft.Data.Sqlite;
using TaskPane.Core.Errors;
using TaskPane.Core.Interfaces;

namespace TaskPane.Infrastructure.Sqlite;

public sealed class SqliteDatabaseBackupRunner : IDatabaseBackupRunner
{
    private readonly SqliteConnectionFactory _factory;

    public SqliteDatabaseBackupRunner(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public string LiveDatabasePath => _factory.DatabasePath;

    public async Task BackupLiveToAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        var tempPath = destinationPath + ".tmp";
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            await using var source = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = tempPath,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString());
            await destination.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Consistent online backup (not a raw file copy of an open DB).
            source.BackupDatabase(destination);

            await destination.CloseAsync().ConfigureAwait(false);
            await source.CloseAsync().ConfigureAwait(false);

            if (!await VerifyBackupAsync(tempPath, cancellationToken).ConfigureAwait(false))
            {
                throw new BusinessException(
                    ValidationCodes.BackupFailed,
                    "Backup verification failed after SQLite online backup.");
            }

            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            File.Move(tempPath, destinationPath);
        }
        catch (BusinessException)
        {
            TryDelete(tempPath);
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(tempPath);
            throw new BusinessException(
                ValidationCodes.BackupFailed,
                $"Could not create a consistent database backup ({ex.GetType().Name}).");
        }
    }

    public async Task RestoreBackupIntoLiveAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
        if (!File.Exists(backupPath))
        {
            throw new BusinessException(ValidationCodes.BackupNotFound, "Backup file was not found.");
        }

        if (!await VerifyBackupAsync(backupPath, cancellationToken).ConfigureAwait(false))
        {
            throw new BusinessException(ValidationCodes.RestoreFailed, "Backup file failed verification.");
        }

        try
        {
            await using var source = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = backupPath,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString());
            await source.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var destination = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
            source.BackupDatabase(destination);
        }
        catch (BusinessException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new BusinessException(
                ValidationCodes.RestoreFailed,
                "Could not restore the selected backup into the live database.");
        }
    }

    public async Task<bool> VerifyBackupAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
        {
            return false;
        }

        try
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = backupPath,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString());
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var integrity = connection.CreateCommand();
            integrity.CommandText = "PRAGMA integrity_check;";
            var result = Convert.ToString(await integrity.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            await using var schema = connection.CreateCommand();
            schema.CommandText = """
                SELECT COUNT(*) FROM sqlite_master
                WHERE type = 'table' AND name IN ('Tasks', 'schema_version');
                """;
            var count = Convert.ToInt32(await schema.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
            return count == 2;
        }
        catch
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // best effort
        }
    }
}
