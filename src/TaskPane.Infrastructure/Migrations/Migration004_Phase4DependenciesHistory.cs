using Microsoft.Data.Sqlite;

namespace TaskPane.Infrastructure.Migrations;

public sealed class Migration004_Phase4DependenciesHistory : IMigration
{
    public int Version => 4;
    public string Name => "Phase4DependenciesHistory";

    public async Task ApplyAsync(SqliteConnection connection, CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Dependencies (
                Id TEXT NOT NULL PRIMARY KEY,
                PredecessorTaskId TEXT NOT NULL,
                SuccessorTaskId TEXT NOT NULL,
                Kind INTEGER NOT NULL DEFAULT 0,
                CreatedUtc TEXT NOT NULL,
                DeletedUtc TEXT NULL,
                FOREIGN KEY (PredecessorTaskId) REFERENCES Tasks(Id),
                FOREIGN KEY (SuccessorTaskId) REFERENCES Tasks(Id)
            );

            CREATE UNIQUE INDEX IF NOT EXISTS UX_Dependencies_Edge
                ON Dependencies(PredecessorTaskId, SuccessorTaskId, Kind)
                WHERE DeletedUtc IS NULL;

            CREATE INDEX IF NOT EXISTS IX_Dependencies_Predecessor ON Dependencies(PredecessorTaskId);
            CREATE INDEX IF NOT EXISTS IX_Dependencies_Successor ON Dependencies(SuccessorTaskId);

            CREATE TABLE IF NOT EXISTS AuditEvents (
                Id TEXT NOT NULL PRIMARY KEY,
                EntityType TEXT NOT NULL,
                EntityId TEXT NOT NULL,
                Action INTEGER NOT NULL,
                TimestampUtc TEXT NOT NULL,
                BeforeJson TEXT NULL,
                AfterJson TEXT NULL,
                CorrelationId TEXT NULL,
                IsUndone INTEGER NOT NULL DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS IX_AuditEvents_Timestamp ON AuditEvents(TimestampUtc);
            CREATE INDEX IF NOT EXISTS IX_AuditEvents_Entity ON AuditEvents(EntityType, EntityId);
            CREATE INDEX IF NOT EXISTS IX_AuditEvents_Undo ON AuditEvents(IsUndone, TimestampUtc);
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
