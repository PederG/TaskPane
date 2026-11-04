using Microsoft.Data.Sqlite;
using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using TaskPane.Core.Ids;
using TaskPane.Core.Interfaces;

namespace TaskPane.Infrastructure.Sqlite;

public sealed class SqliteDependencyRepository : IDependencyRepository
{
    private readonly SqliteUnitOfWork _unitOfWork;

    public SqliteDependencyRepository(SqliteUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<DependencyEdge?> GetByIdAsync(DependencyId id, CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = "SELECT * FROM Dependencies WHERE Id = $id LIMIT 1;";
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

    public async Task<IReadOnlyList<DependencyEdge>> ListAsync(
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = includeDeleted
                ? "SELECT * FROM Dependencies;"
                : "SELECT * FROM Dependencies WHERE DeletedUtc IS NULL;";
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

    public async Task<IReadOnlyList<DependencyEdge>> ListForTaskAsync(
        TaskId taskId,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = includeDeleted
                ? """
                  SELECT * FROM Dependencies
                  WHERE PredecessorTaskId = $id OR SuccessorTaskId = $id;
                  """
                : """
                  SELECT * FROM Dependencies
                  WHERE (PredecessorTaskId = $id OR SuccessorTaskId = $id)
                    AND DeletedUtc IS NULL;
                  """;
            cmd.Parameters.AddWithValue("$id", taskId.ToString());
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

    public async Task<DependencyEdge?> FindEdgeAsync(
        TaskId predecessorId,
        TaskId successorId,
        DependencyKind kind,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = includeDeleted
                ? """
                  SELECT * FROM Dependencies
                  WHERE PredecessorTaskId = $p AND SuccessorTaskId = $s AND Kind = $k
                  LIMIT 1;
                  """
                : """
                  SELECT * FROM Dependencies
                  WHERE PredecessorTaskId = $p AND SuccessorTaskId = $s AND Kind = $k
                    AND DeletedUtc IS NULL
                  LIMIT 1;
                  """;
            cmd.Parameters.AddWithValue("$p", predecessorId.ToString());
            cmd.Parameters.AddWithValue("$s", successorId.ToString());
            cmd.Parameters.AddWithValue("$k", (int)kind);
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

    public async Task InsertAsync(DependencyEdge edge, CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = """
                INSERT INTO Dependencies (Id, PredecessorTaskId, SuccessorTaskId, Kind, CreatedUtc, DeletedUtc)
                VALUES ($id, $p, $s, $k, $c, $d);
                """;
            Bind(cmd, edge);
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

    public async Task UpdateAsync(DependencyEdge edge, CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = """
                UPDATE Dependencies
                SET PredecessorTaskId = $p,
                    SuccessorTaskId = $s,
                    Kind = $k,
                    CreatedUtc = $c,
                    DeletedUtc = $d
                WHERE Id = $id;
                """;
            Bind(cmd, edge);
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

    public async Task SoftDeleteAsync(DependencyId id, DateTimeOffset deletedUtc, CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = "UPDATE Dependencies SET DeletedUtc = $d WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$id", id.ToString());
            cmd.Parameters.AddWithValue("$d", deletedUtc.ToString("O"));
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

    private static void Bind(SqliteCommand cmd, DependencyEdge edge)
    {
        cmd.Parameters.AddWithValue("$id", edge.Id.ToString());
        cmd.Parameters.AddWithValue("$p", edge.PredecessorTaskId.ToString());
        cmd.Parameters.AddWithValue("$s", edge.SuccessorTaskId.ToString());
        cmd.Parameters.AddWithValue("$k", (int)edge.Kind);
        cmd.Parameters.AddWithValue("$c", edge.CreatedUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$d", (object?)edge.DeletedUtc?.ToString("O") ?? DBNull.Value);
    }

    private static async Task<IReadOnlyList<DependencyEdge>> ReadAllAsync(
        SqliteCommand cmd,
        CancellationToken cancellationToken)
    {
        var list = new List<DependencyEdge>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(Map(reader));
        }

        return list;
    }

    private static DependencyEdge Map(SqliteDataReader reader) => new()
    {
        Id = DependencyId.Parse(reader.GetString(reader.GetOrdinal("Id"))),
        PredecessorTaskId = TaskId.Parse(reader.GetString(reader.GetOrdinal("PredecessorTaskId"))),
        SuccessorTaskId = TaskId.Parse(reader.GetString(reader.GetOrdinal("SuccessorTaskId"))),
        Kind = (DependencyKind)reader.GetInt32(reader.GetOrdinal("Kind")),
        CreatedUtc = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("CreatedUtc"))),
        DeletedUtc = reader.IsDBNull(reader.GetOrdinal("DeletedUtc"))
            ? null
            : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("DeletedUtc")))
    };
}
