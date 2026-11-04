using Microsoft.Data.Sqlite;

namespace TaskPane.Infrastructure.Migrations;

public sealed class Migration001_InitialSchema : IMigration
{
    public int Version => 1;
    public string Name => "InitialSchema";

    public async Task ApplyAsync(SqliteConnection connection, CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_version (
                Version INTEGER NOT NULL PRIMARY KEY,
                Name TEXT NOT NULL,
                AppliedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Tasks (
                Id TEXT NOT NULL PRIMARY KEY,
                ParentTaskId TEXT NULL,
                Title TEXT NOT NULL,
                Notes TEXT NULL,
                Priority INTEGER NOT NULL DEFAULT 1,
                FocusBucket INTEGER NOT NULL DEFAULT 0,
                Status INTEGER NOT NULL DEFAULT 0,
                ManualSortKey REAL NOT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                CompletedUtc TEXT NULL,
                DeletedUtc TEXT NULL,
                DeletionBatchId TEXT NULL,
                Revision INTEGER NOT NULL DEFAULT 1,
                FOREIGN KEY (ParentTaskId) REFERENCES Tasks(Id)
            );

            CREATE INDEX IF NOT EXISTS IX_Tasks_Parent ON Tasks(ParentTaskId);
            CREATE INDEX IF NOT EXISTS IX_Tasks_Focus ON Tasks(FocusBucket);
            CREATE INDEX IF NOT EXISTS IX_Tasks_Deleted ON Tasks(DeletedUtc);
            CREATE INDEX IF NOT EXISTS IX_Tasks_DeletionBatch ON Tasks(DeletionBatchId);

            CREATE TABLE IF NOT EXISTS Settings (
                Key TEXT NOT NULL PRIMARY KEY,
                JsonValue TEXT NOT NULL
            );
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
