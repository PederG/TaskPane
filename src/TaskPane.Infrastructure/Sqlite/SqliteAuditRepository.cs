using Microsoft.Data.Sqlite;
using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using TaskPane.Core.Ids;
using TaskPane.Core.Interfaces;

namespace TaskPane.Infrastructure.Sqlite;

public sealed class SqliteAuditRepository : IAuditRepository
{
    private readonly SqliteUnitOfWork _unitOfWork;

    public SqliteAuditRepository(SqliteUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task InsertAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = """
                INSERT INTO AuditEvents
                    (Id, EntityType, EntityId, Action, TimestampUtc, BeforeJson, AfterJson, CorrelationId, IsUndone)
                VALUES
                    ($id, $et, $eid, $a, $t, $b, $af, $c, $u);
                """;
            Bind(cmd, auditEvent);
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

    public async Task UpdateAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = """
                UPDATE AuditEvents
                SET EntityType = $et,
                    EntityId = $eid,
                    Action = $a,
                    TimestampUtc = $t,
                    BeforeJson = $b,
                    AfterJson = $af,
                    CorrelationId = $c,
                    IsUndone = $u
                WHERE Id = $id;
                """;
            Bind(cmd, auditEvent);
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

    public async Task<AuditEvent?> GetByIdAsync(AuditEventId id, CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = "SELECT * FROM AuditEvents WHERE Id = $id LIMIT 1;";
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

    public async Task<IReadOnlyList<AuditEvent>> ListRecentAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = """
                SELECT * FROM AuditEvents
                ORDER BY TimestampUtc DESC
                LIMIT $limit;
                """;
            cmd.Parameters.AddWithValue("$limit", limit);
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

    public async Task<IReadOnlyList<AuditEvent>> ListForEntityAsync(
        string entityType,
        string entityId,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = """
                SELECT * FROM AuditEvents
                WHERE EntityType = $et AND EntityId = $eid
                ORDER BY TimestampUtc DESC
                LIMIT $limit;
                """;
            cmd.Parameters.AddWithValue("$et", entityType);
            cmd.Parameters.AddWithValue("$eid", entityId);
            cmd.Parameters.AddWithValue("$limit", limit);
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

    public async Task<AuditEvent?> GetLatestUndoableAsync(CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = """
                SELECT * FROM AuditEvents
                WHERE IsUndone = 0
                  AND Action IN ($c, $s, $m, $d)
                ORDER BY TimestampUtc DESC
                LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("$c", (int)AuditAction.Completed);
            cmd.Parameters.AddWithValue("$s", (int)AuditAction.Slept);
            cmd.Parameters.AddWithValue("$m", (int)AuditAction.Moved);
            cmd.Parameters.AddWithValue("$d", (int)AuditAction.SoftDeleted);
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

    public async Task<IReadOnlyList<AuditEvent>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = """
                SELECT * FROM AuditEvents
                ORDER BY TimestampUtc ASC;
                """;
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

    private static void Bind(SqliteCommand cmd, AuditEvent auditEvent)
    {
        cmd.Parameters.AddWithValue("$id", auditEvent.Id.ToString());
        cmd.Parameters.AddWithValue("$et", auditEvent.EntityType);
        cmd.Parameters.AddWithValue("$eid", auditEvent.EntityId);
        cmd.Parameters.AddWithValue("$a", (int)auditEvent.Action);
        cmd.Parameters.AddWithValue("$t", auditEvent.TimestampUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$b", (object?)auditEvent.BeforeJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$af", (object?)auditEvent.AfterJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$c", (object?)auditEvent.CorrelationId?.ToString("D") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$u", auditEvent.IsUndone ? 1 : 0);
    }

    private static async Task<IReadOnlyList<AuditEvent>> ReadAllAsync(
        SqliteCommand cmd,
        CancellationToken cancellationToken)
    {
        var list = new List<AuditEvent>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(Map(reader));
        }

        return list;
    }

    private static AuditEvent Map(SqliteDataReader reader) => new()
    {
        Id = AuditEventId.Parse(reader.GetString(reader.GetOrdinal("Id"))),
        EntityType = reader.GetString(reader.GetOrdinal("EntityType")),
        EntityId = reader.GetString(reader.GetOrdinal("EntityId")),
        Action = (AuditAction)reader.GetInt32(reader.GetOrdinal("Action")),
        TimestampUtc = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("TimestampUtc"))),
        BeforeJson = reader.IsDBNull(reader.GetOrdinal("BeforeJson"))
            ? null
            : reader.GetString(reader.GetOrdinal("BeforeJson")),
        AfterJson = reader.IsDBNull(reader.GetOrdinal("AfterJson"))
            ? null
            : reader.GetString(reader.GetOrdinal("AfterJson")),
        CorrelationId = reader.IsDBNull(reader.GetOrdinal("CorrelationId"))
            ? null
            : Guid.Parse(reader.GetString(reader.GetOrdinal("CorrelationId"))),
        IsUndone = reader.GetInt32(reader.GetOrdinal("IsUndone")) == 1
    };
}
