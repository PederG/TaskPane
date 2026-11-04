using Microsoft.Data.Sqlite;

namespace TaskPane.Infrastructure.Migrations;

public sealed class Migration006_Phase6BackupTransfer : IMigration
{
    public int Version => 6;
    public string Name => "Phase6BackupTransfer";

    public async Task ApplyAsync(SqliteConnection connection, CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS BackupHistory (
                Id TEXT NOT NULL PRIMARY KEY,
                CreatedUtc TEXT NOT NULL,
                LocalPath TEXT NOT NULL,
                DocumentsPath TEXT NULL,
                Result INTEGER NOT NULL,
                ErrorCode TEXT NULL,
                Message TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_BackupHistory_Created
                ON BackupHistory(CreatedUtc DESC);
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
