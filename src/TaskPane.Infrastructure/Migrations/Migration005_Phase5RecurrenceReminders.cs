using Microsoft.Data.Sqlite;

namespace TaskPane.Infrastructure.Migrations;

public sealed class Migration005_Phase5RecurrenceReminders : IMigration
{
    public int Version => 5;
    public string Name => "Phase5RecurrenceReminders";

    public async Task ApplyAsync(SqliteConnection connection, CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            ALTER TABLE Tasks ADD COLUMN RecurrenceTemplateId TEXT NULL;
            ALTER TABLE Tasks ADD COLUMN OccurrenceKey TEXT NULL;

            CREATE TABLE IF NOT EXISTS RecurrenceTemplates (
                Id TEXT NOT NULL PRIMARY KEY,
                SourceTaskId TEXT NOT NULL,
                RuleJson TEXT NOT NULL,
                ContentSnapshotJson TEXT NOT NULL,
                TimeZoneId TEXT NOT NULL,
                NextEvaluationUtc TEXT NULL,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                LastSkippedOccurrenceKey TEXT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                FOREIGN KEY (SourceTaskId) REFERENCES Tasks(Id)
            );

            CREATE INDEX IF NOT EXISTS IX_RecurrenceTemplates_Enabled
                ON RecurrenceTemplates(IsEnabled, NextEvaluationUtc);

            CREATE UNIQUE INDEX IF NOT EXISTS UX_Tasks_Occurrence
                ON Tasks(RecurrenceTemplateId, OccurrenceKey)
                WHERE RecurrenceTemplateId IS NOT NULL AND OccurrenceKey IS NOT NULL;

            CREATE INDEX IF NOT EXISTS IX_Tasks_RecurrenceTemplate
                ON Tasks(RecurrenceTemplateId);

            CREATE TABLE IF NOT EXISTS Reminders (
                Id TEXT NOT NULL PRIMARY KEY,
                TaskId TEXT NOT NULL,
                DueUtc TEXT NOT NULL,
                DeliveredUtc TEXT NULL,
                DismissedUtc TEXT NULL,
                SnoozedUntilUtc TEXT NULL,
                Kind INTEGER NOT NULL DEFAULT 0,
                DeliveryKey TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL,
                FOREIGN KEY (TaskId) REFERENCES Tasks(Id)
            );

            CREATE UNIQUE INDEX IF NOT EXISTS UX_Reminders_DeliveryKey ON Reminders(DeliveryKey);
            CREATE INDEX IF NOT EXISTS IX_Reminders_Task ON Reminders(TaskId);
            CREATE INDEX IF NOT EXISTS IX_Reminders_Due ON Reminders(DueUtc);
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
