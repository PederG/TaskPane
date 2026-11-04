using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using TaskPane.Core.Ids;
using TaskPane.Core.Interfaces;

namespace TaskPane.Infrastructure.Sqlite;

public sealed class SqliteTimeSessionRepository : ITimeSessionRepository
{
    private readonly SqliteUnitOfWork _unitOfWork;

    public SqliteTimeSessionRepository(SqliteUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<TimeSession?> GetByIdAsync(SessionId id, CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = """
                SELECT Id, TaskId, StartedUtc, EndedUtc, EndReason, IsDiscarded, OpenSlot, CreatedUtc, UpdatedUtc
                FROM TimeSessions WHERE Id = $id LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("$id", id.ToString());
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Map(reader) : null;
        }
        finally
        {
            if (owns)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task<TimeSession?> GetOpenSessionAsync(CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = """
                SELECT Id, TaskId, StartedUtc, EndedUtc, EndReason, IsDiscarded, OpenSlot, CreatedUtc, UpdatedUtc
                FROM TimeSessions WHERE OpenSlot = 1 AND EndedUtc IS NULL LIMIT 1;
                """;
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Map(reader) : null;
        }
        finally
        {
            if (owns)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public Task<IReadOnlyList<TimeSession>> ListForTaskAsync(
        TaskId taskId,
        bool includeDiscarded = false,
        CancellationToken cancellationToken = default)
        => ListForTasksAsync([taskId], includeDiscarded, cancellationToken);

    public async Task<IReadOnlyList<TimeSession>> ListForTasksAsync(
        IReadOnlyList<TaskId> taskIds,
        bool includeDiscarded = false,
        CancellationToken cancellationToken = default)
    {
        if (taskIds.Count == 0)
        {
            return [];
        }

        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            var placeholders = string.Join(',', taskIds.Select((_, i) => $"$t{i}"));
            cmd.CommandText = $"""
                SELECT Id, TaskId, StartedUtc, EndedUtc, EndReason, IsDiscarded, OpenSlot, CreatedUtc, UpdatedUtc
                FROM TimeSessions
                WHERE TaskId IN ({placeholders})
                {(includeDiscarded ? "" : "AND IsDiscarded = 0")}
                ORDER BY StartedUtc ASC;
                """;
            for (var i = 0; i < taskIds.Count; i++)
            {
                cmd.Parameters.AddWithValue($"$t{i}", taskIds[i].ToString());
            }

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

    public async Task<IReadOnlyList<TimeSession>> ListInRangeAsync(
        DateTimeOffset rangeStartUtc,
        DateTimeOffset rangeEndUtc,
        bool includeDiscarded = false,
        CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = $"""
                SELECT Id, TaskId, StartedUtc, EndedUtc, EndReason, IsDiscarded, OpenSlot, CreatedUtc, UpdatedUtc
                FROM TimeSessions
                WHERE StartedUtc < $end
                  AND (EndedUtc IS NULL OR EndedUtc > $start)
                {(includeDiscarded ? "" : "AND IsDiscarded = 0")}
                ORDER BY StartedUtc ASC;
                """;
            cmd.Parameters.AddWithValue("$start", rangeStartUtc.ToString("O"));
            cmd.Parameters.AddWithValue("$end", rangeEndUtc.ToString("O"));
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

    public async Task InsertAsync(TimeSession session, CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = """
                INSERT INTO TimeSessions
                    (Id, TaskId, StartedUtc, EndedUtc, EndReason, IsDiscarded, OpenSlot, CreatedUtc, UpdatedUtc)
                VALUES
                    ($id, $task, $start, $end, $reason, $discarded, $slot, $created, $updated);
                """;
            Bind(cmd, session);
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

    public async Task UpdateAsync(TimeSession session, CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = """
                UPDATE TimeSessions SET
                    TaskId = $task,
                    StartedUtc = $start,
                    EndedUtc = $end,
                    EndReason = $reason,
                    IsDiscarded = $discarded,
                    OpenSlot = $slot,
                    UpdatedUtc = $updated
                WHERE Id = $id;
                """;
            Bind(cmd, session);
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

    public async Task DeleteAsync(SessionId id, CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = "DELETE FROM TimeSessions WHERE Id = $id;";
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

    private static async Task<IReadOnlyList<TimeSession>> ReadAllAsync(
        Microsoft.Data.Sqlite.SqliteCommand cmd,
        CancellationToken cancellationToken)
    {
        var list = new List<TimeSession>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(Map(reader));
        }

        return list;
    }

    private static void Bind(Microsoft.Data.Sqlite.SqliteCommand cmd, TimeSession session)
    {
        cmd.Parameters.AddWithValue("$id", session.Id.ToString());
        cmd.Parameters.AddWithValue("$task", session.TaskId.ToString());
        cmd.Parameters.AddWithValue("$start", session.StartedUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$end", (object?)session.EndedUtc?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$reason", (object?)session.EndReason?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$discarded", session.IsDiscarded ? 1 : 0);
        cmd.Parameters.AddWithValue("$slot", (object?)session.OpenSlot ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", session.CreatedUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$updated", session.UpdatedUtc.ToString("O"));
    }

    private static TimeSession Map(Microsoft.Data.Sqlite.SqliteDataReader reader)
    {
        SessionEndReason? reason = null;
        if (!reader.IsDBNull(4))
        {
            reason = Enum.Parse<SessionEndReason>(reader.GetString(4));
        }

        return new TimeSession
        {
            Id = SessionId.Parse(reader.GetString(0)),
            TaskId = TaskId.Parse(reader.GetString(1)),
            StartedUtc = DateTimeOffset.Parse(reader.GetString(2)),
            EndedUtc = reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3)),
            EndReason = reason,
            IsDiscarded = reader.GetInt64(5) != 0,
            OpenSlot = reader.IsDBNull(6) ? null : (int)reader.GetInt64(6),
            CreatedUtc = DateTimeOffset.Parse(reader.GetString(7)),
            UpdatedUtc = DateTimeOffset.Parse(reader.GetString(8))
        };
    }
}
