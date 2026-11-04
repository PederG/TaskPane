using Microsoft.Data.Sqlite;
using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using TaskPane.Core.Ids;
using TaskPane.Core.Interfaces;

namespace TaskPane.Infrastructure.Sqlite;

public sealed class SqliteReminderRepository : IReminderRepository
{
    private readonly SqliteUnitOfWork _unitOfWork;

    public SqliteReminderRepository(SqliteUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Reminder?> GetByIdAsync(ReminderId id, CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = "SELECT * FROM Reminders WHERE Id = $id LIMIT 1;";
            cmd.Parameters.AddWithValue("$id", id.ToString());
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            return Map(reader);
        }
        finally
        {
            if (owns)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task<Reminder?> GetByDeliveryKeyAsync(
        string deliveryKey,
        CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = "SELECT * FROM Reminders WHERE DeliveryKey = $k LIMIT 1;";
            cmd.Parameters.AddWithValue("$k", deliveryKey);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            return Map(reader);
        }
        finally
        {
            if (owns)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task<IReadOnlyList<Reminder>> ListForTaskAsync(
        TaskId taskId,
        CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = "SELECT * FROM Reminders WHERE TaskId = $t ORDER BY DueUtc;";
            cmd.Parameters.AddWithValue("$t", taskId.ToString());
            return await ReadAllAsync(cmd, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (owns)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task<IReadOnlyList<Reminder>> ListDueAsync(
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = """
                SELECT * FROM Reminders
                WHERE DismissedUtc IS NULL
                  AND DeliveredUtc IS NULL
                  AND DueUtc <= $now
                ORDER BY DueUtc;
                """;
            cmd.Parameters.AddWithValue("$now", utcNow.ToString("O"));
            return await ReadAllAsync(cmd, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (owns)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task InsertAsync(Reminder reminder, CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = """
                INSERT INTO Reminders (
                    Id, TaskId, DueUtc, DeliveredUtc, DismissedUtc, SnoozedUntilUtc,
                    Kind, DeliveryKey, CreatedUtc)
                VALUES (
                    $id, $task, $due, $delivered, $dismissed, $snoozed,
                    $kind, $key, $created);
                """;
            Bind(cmd, reminder);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (owns)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task UpdateAsync(Reminder reminder, CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = """
                UPDATE Reminders SET
                    TaskId = $task,
                    DueUtc = $due,
                    DeliveredUtc = $delivered,
                    DismissedUtc = $dismissed,
                    SnoozedUntilUtc = $snoozed,
                    Kind = $kind,
                    DeliveryKey = $key,
                    CreatedUtc = $created
                WHERE Id = $id;
                """;
            Bind(cmd, reminder);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (owns)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task DeleteAsync(ReminderId id, CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = "DELETE FROM Reminders WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$id", id.ToString());
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (owns)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task<IReadOnlyList<Reminder>> ReadAllAsync(
        SqliteCommand cmd,
        CancellationToken cancellationToken)
    {
        var list = new List<Reminder>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(Map(reader));
        }

        return list;
    }

    private static void Bind(SqliteCommand cmd, Reminder reminder)
    {
        cmd.Parameters.AddWithValue("$id", reminder.Id.ToString());
        cmd.Parameters.AddWithValue("$task", reminder.TaskId.ToString());
        cmd.Parameters.AddWithValue("$due", reminder.DueUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$delivered", (object?)reminder.DeliveredUtc?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$dismissed", (object?)reminder.DismissedUtc?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$snoozed", (object?)reminder.SnoozedUntilUtc?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$kind", (int)reminder.Kind);
        cmd.Parameters.AddWithValue("$key", reminder.DeliveryKey);
        cmd.Parameters.AddWithValue("$created", reminder.CreatedUtc.ToString("O"));
    }

    private static Reminder Map(SqliteDataReader reader)
    {
        string? delivered = reader["DeliveredUtc"] is DBNull ? null : Convert.ToString(reader["DeliveredUtc"]);
        string? dismissed = reader["DismissedUtc"] is DBNull ? null : Convert.ToString(reader["DismissedUtc"]);
        string? snoozed = reader["SnoozedUntilUtc"] is DBNull ? null : Convert.ToString(reader["SnoozedUntilUtc"]);

        return new Reminder
        {
            Id = ReminderId.Parse(Convert.ToString(reader["Id"])!),
            TaskId = TaskId.Parse(Convert.ToString(reader["TaskId"])!),
            DueUtc = DateTimeOffset.Parse(Convert.ToString(reader["DueUtc"])!),
            DeliveredUtc = delivered is null ? null : DateTimeOffset.Parse(delivered),
            DismissedUtc = dismissed is null ? null : DateTimeOffset.Parse(dismissed),
            SnoozedUntilUtc = snoozed is null ? null : DateTimeOffset.Parse(snoozed),
            Kind = (ReminderKind)Convert.ToInt32(reader["Kind"]),
            DeliveryKey = Convert.ToString(reader["DeliveryKey"])!,
            CreatedUtc = DateTimeOffset.Parse(Convert.ToString(reader["CreatedUtc"])!)
        };
    }
}
