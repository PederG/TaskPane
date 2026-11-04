using Microsoft.Data.Sqlite;

namespace TaskPane.Infrastructure.Migrations;

public sealed class Migration003_Phase3TimeTracking : IMigration
{
    public int Version => 3;
    public string Name => "Phase3TimeTracking";

    public async Task ApplyAsync(SqliteConnection connection, CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS TimeSessions (
                Id TEXT NOT NULL PRIMARY KEY,
                TaskId TEXT NOT NULL,
                StartedUtc TEXT NOT NULL,
                EndedUtc TEXT NULL,
                EndReason TEXT NULL,
                IsDiscarded INTEGER NOT NULL DEFAULT 0,
                OpenSlot INTEGER NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                FOREIGN KEY (TaskId) REFERENCES Tasks(Id)
            );

            -- At most one open session in the local database.
            CREATE UNIQUE INDEX IF NOT EXISTS UX_TimeSessions_OneOpen
                ON TimeSessions(OpenSlot)
                WHERE OpenSlot = 1;

            CREATE INDEX IF NOT EXISTS IX_TimeSessions_Task ON TimeSessions(TaskId);
            CREATE INDEX IF NOT EXISTS IX_TimeSessions_Started ON TimeSessions(StartedUtc);
            CREATE INDEX IF NOT EXISTS IX_TimeSessions_Ended ON TimeSessions(EndedUtc);
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
