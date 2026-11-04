using Microsoft.Data.Sqlite;
using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using TaskPane.Core.Ids;
using TaskPane.Core.Interfaces;

namespace TaskPane.Infrastructure.Sqlite;

public sealed class SqliteTaskRepository : ITaskRepository
{
    private readonly SqliteUnitOfWork _unitOfWork;

    public SqliteTaskRepository(SqliteUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<TaskItem?> GetByIdAsync(TaskId id, CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = "SELECT * FROM Tasks WHERE Id = $id LIMIT 1;";
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

    public async Task<IReadOnlyList<TaskItem>> ListAsync(
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
                ? "SELECT * FROM Tasks;"
                : "SELECT * FROM Tasks WHERE DeletedUtc IS NULL;";
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

    public async Task<IReadOnlyList<TaskItem>> ListChildrenAsync(
        TaskId parentId,
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
                ? "SELECT * FROM Tasks WHERE ParentTaskId = $p;"
                : "SELECT * FROM Tasks WHERE ParentTaskId = $p AND DeletedUtc IS NULL;";
            cmd.Parameters.AddWithValue("$p", parentId.ToString());
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

    public async Task InsertAsync(TaskItem task, CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = """
                INSERT INTO Tasks (
                    Id, ParentTaskId, Title, Notes, Priority, FocusBucket, Status,
                    ManualSortKey, CreatedUtc, UpdatedUtc, CompletedUtc, DeletedUtc,
                    DeletionBatchId, Revision)
                VALUES (
                    $id, $parent, $title, $notes, $priority, $focus, $status,
                    $sort, $created, $updated, $completed, $deleted, $batch, $rev);
                """;
            Bind(cmd, task);
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

    public async Task UpdateAsync(TaskItem task, CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = """
                UPDATE Tasks SET
                    ParentTaskId = $parent,
                    Title = $title,
                    Notes = $notes,
                    Priority = $priority,
                    FocusBucket = $focus,
                    Status = $status,
                    ManualSortKey = $sort,
                    CreatedUtc = $created,
                    UpdatedUtc = $updated,
                    CompletedUtc = $completed,
                    DeletedUtc = $deleted,
                    DeletionBatchId = $batch,
                    Revision = $rev
                WHERE Id = $id;
                """;
            Bind(cmd, task);
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

    public async Task SoftDeleteBatchAsync(
        IReadOnlyList<TaskId> ids,
        Guid deletionBatchId,
        DateTimeOffset deletedUtc,
        CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return;
        }

        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            foreach (var id in ids)
            {
                await using var cmd = connection.CreateCommand();
                cmd.Transaction = _unitOfWork.AmbientTransaction;
                cmd.CommandText = """
                    UPDATE Tasks
                    SET DeletedUtc = $d, DeletionBatchId = $b, UpdatedUtc = $d, Revision = Revision + 1
                    WHERE Id = $id AND DeletedUtc IS NULL;
                    """;
                cmd.Parameters.AddWithValue("$d", deletedUtc.ToString("O"));
                cmd.Parameters.AddWithValue("$b", deletionBatchId.ToString("D"));
                cmd.Parameters.AddWithValue("$id", id.ToString());
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (owns)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task RestoreBatchAsync(Guid deletionBatchId, CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = """
                UPDATE Tasks
                SET DeletedUtc = NULL, DeletionBatchId = NULL, UpdatedUtc = $u, Revision = Revision + 1
                WHERE DeletionBatchId = $b;
                """;
            cmd.Parameters.AddWithValue("$u", DateTimeOffset.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("$b", deletionBatchId.ToString("D"));
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

    public async Task<IReadOnlyList<TaskItem>> ListByDeletionBatchAsync(
        Guid deletionBatchId,
        CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = "SELECT * FROM Tasks WHERE DeletionBatchId = $b;";
            cmd.Parameters.AddWithValue("$b", deletionBatchId.ToString("D"));
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

    public async Task<double> GetNextSortKeyAsync(
        TaskId? parentId,
        FocusBucket? focusBucket,
        CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;

            if (parentId is { } pid)
            {
                cmd.CommandText = """
                    SELECT COALESCE(MAX(ManualSortKey), 0) + 1000
                    FROM Tasks
                    WHERE ParentTaskId = $p AND DeletedUtc IS NULL;
                    """;
                cmd.Parameters.AddWithValue("$p", pid.ToString());
            }
            else if (focusBucket is { } focus)
            {
                cmd.CommandText = """
                    SELECT COALESCE(MAX(ManualSortKey), 0) + 1000
                    FROM Tasks
                    WHERE ParentTaskId IS NULL AND FocusBucket = $f AND DeletedUtc IS NULL;
                    """;
                cmd.Parameters.AddWithValue("$f", (int)focus);
            }
            else
            {
                cmd.CommandText = """
                    SELECT COALESCE(MAX(ManualSortKey), 0) + 1000
                    FROM Tasks
                    WHERE ParentTaskId IS NULL AND DeletedUtc IS NULL;
                    """;
            }

            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return Convert.ToDouble(result);
        }
        finally
        {
            if (owns)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task<IReadOnlyList<TaskItem>> ReadAllAsync(
        SqliteCommand cmd,
        CancellationToken cancellationToken)
    {
        var list = new List<TaskItem>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(Map(reader));
        }

        return list;
    }

    private static void Bind(SqliteCommand cmd, TaskItem task)
    {
        cmd.Parameters.AddWithValue("$id", task.Id.ToString());
        cmd.Parameters.AddWithValue("$parent", (object?)task.ParentTaskId?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$title", task.Title);
        cmd.Parameters.AddWithValue("$notes", (object?)task.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$priority", (int)task.Priority);
        cmd.Parameters.AddWithValue("$focus", (int)task.FocusBucket);
        cmd.Parameters.AddWithValue("$status", (int)task.Status);
        cmd.Parameters.AddWithValue("$sort", task.ManualSortKey);
        cmd.Parameters.AddWithValue("$created", task.CreatedUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$updated", task.UpdatedUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$completed", (object?)task.CompletedUtc?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$deleted", (object?)task.DeletedUtc?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$batch", (object?)task.DeletionBatchId?.ToString("D") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$rev", task.Revision);
    }

    private static TaskItem Map(SqliteDataReader reader)
    {
        string? parent = reader["ParentTaskId"] is DBNull ? null : Convert.ToString(reader["ParentTaskId"]);
        string? completed = reader["CompletedUtc"] is DBNull ? null : Convert.ToString(reader["CompletedUtc"]);
        string? deleted = reader["DeletedUtc"] is DBNull ? null : Convert.ToString(reader["DeletedUtc"]);
        string? batch = reader["DeletionBatchId"] is DBNull ? null : Convert.ToString(reader["DeletionBatchId"]);
        string? notes = reader["Notes"] is DBNull ? null : Convert.ToString(reader["Notes"]);

        return new TaskItem
        {
            Id = TaskId.Parse(Convert.ToString(reader["Id"])!),
            ParentTaskId = parent is null ? null : TaskId.Parse(parent),
            Title = Convert.ToString(reader["Title"])!,
            Notes = notes,
            Priority = (TaskPriority)Convert.ToInt32(reader["Priority"]),
            FocusBucket = (FocusBucket)Convert.ToInt32(reader["FocusBucket"]),
            Status = (TaskLifecycleStatus)Convert.ToInt32(reader["Status"]),
            ManualSortKey = Convert.ToDouble(reader["ManualSortKey"]),
            CreatedUtc = DateTimeOffset.Parse(Convert.ToString(reader["CreatedUtc"])!),
            UpdatedUtc = DateTimeOffset.Parse(Convert.ToString(reader["UpdatedUtc"])!),
            CompletedUtc = completed is null ? null : DateTimeOffset.Parse(completed),
            DeletedUtc = deleted is null ? null : DateTimeOffset.Parse(deleted),
            DeletionBatchId = batch is null ? null : Guid.Parse(batch),
            Revision = Convert.ToInt64(reader["Revision"])
        };
    }
}
