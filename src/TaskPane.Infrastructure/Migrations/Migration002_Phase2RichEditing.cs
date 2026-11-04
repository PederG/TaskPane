using Microsoft.Data.Sqlite;

namespace TaskPane.Infrastructure.Migrations;

public sealed class Migration002_Phase2RichEditing : IMigration
{
    public int Version => 2;
    public string Name => "Phase2RichEditing";

    public async Task ApplyAsync(SqliteConnection connection, CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            ALTER TABLE Tasks ADD COLUMN DeadlineLocalDate TEXT NULL;
            ALTER TABLE Tasks ADD COLUMN DeadlineLocalTime TEXT NULL;
            ALTER TABLE Tasks ADD COLUMN DeadlineTimeZoneId TEXT NULL;
            ALTER TABLE Tasks ADD COLUMN SleepUntilUtc TEXT NULL;
            ALTER TABLE Tasks ADD COLUMN SleepIndefinitely INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE Tasks ADD COLUMN WaitingFor TEXT NULL;
            ALTER TABLE Tasks ADD COLUMN WaitingFollowUpUtc TEXT NULL;
            ALTER TABLE Tasks ADD COLUMN ProjectId TEXT NULL;

            CREATE TABLE IF NOT EXISTS Projects (
                Id TEXT NOT NULL PRIMARY KEY,
                Name TEXT NOT NULL,
                Color TEXT NULL,
                ArchivedUtc TEXT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Tags (
                Id TEXT NOT NULL PRIMARY KEY,
                Name TEXT NOT NULL COLLATE NOCASE,
                Color TEXT NULL,
                CreatedUtc TEXT NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS UX_Tags_Name ON Tags(Name COLLATE NOCASE);

            CREATE TABLE IF NOT EXISTS TaskTags (
                TaskId TEXT NOT NULL,
                TagId TEXT NOT NULL,
                PRIMARY KEY (TaskId, TagId),
                FOREIGN KEY (TaskId) REFERENCES Tasks(Id),
                FOREIGN KEY (TagId) REFERENCES Tags(Id)
            );

            CREATE INDEX IF NOT EXISTS IX_Tasks_Project ON Tasks(ProjectId);
            CREATE INDEX IF NOT EXISTS IX_Tasks_SleepUntil ON Tasks(SleepUntilUtc);
            CREATE INDEX IF NOT EXISTS IX_TaskTags_Tag ON TaskTags(TagId);
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
