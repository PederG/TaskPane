using Microsoft.Data.Sqlite;
using TaskPane.Core.Entities;
using TaskPane.Core.Ids;
using TaskPane.Core.Interfaces;

namespace TaskPane.Infrastructure.Sqlite;

public sealed class SqliteTagRepository : ITagRepository
{
    private readonly SqliteUnitOfWork _unitOfWork;

    public SqliteTagRepository(SqliteUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Tag?> GetByIdAsync(TagId id, CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = "SELECT * FROM Tags WHERE Id = $id LIMIT 1;";
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

    public async Task<Tag?> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = "SELECT * FROM Tags WHERE Name = $n COLLATE NOCASE LIMIT 1;";
            cmd.Parameters.AddWithValue("$n", name.Trim());
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

    public async Task<IReadOnlyList<Tag>> ListAsync(CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = "SELECT * FROM Tags ORDER BY Name COLLATE NOCASE;";
            var list = new List<Tag>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                list.Add(Map(reader));
            }

            return list;
        }
        finally
        {
            if (owns)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task InsertAsync(Tag tag, CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = """
                INSERT INTO Tags (Id, Name, Color, CreatedUtc)
                VALUES ($id, $name, $color, $created);
                """;
            cmd.Parameters.AddWithValue("$id", tag.Id.ToString());
            cmd.Parameters.AddWithValue("$name", tag.Name);
            cmd.Parameters.AddWithValue("$color", (object?)tag.Color ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$created", tag.CreatedUtc.ToString("O"));
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

    public async Task UpdateAsync(Tag tag, CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = """
                UPDATE Tags SET Name = $name, Color = $color WHERE Id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", tag.Id.ToString());
            cmd.Parameters.AddWithValue("$name", tag.Name);
            cmd.Parameters.AddWithValue("$color", (object?)tag.Color ?? DBNull.Value);
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

    public async Task SetTaskTagsAsync(
        TaskId taskId,
        IReadOnlyList<TagId> tagIds,
        CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using (var clear = connection.CreateCommand())
            {
                clear.Transaction = _unitOfWork.AmbientTransaction;
                clear.CommandText = "DELETE FROM TaskTags WHERE TaskId = $t;";
                clear.Parameters.AddWithValue("$t", taskId.ToString());
                await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            foreach (var tagId in tagIds.Distinct())
            {
                await using var insert = connection.CreateCommand();
                insert.Transaction = _unitOfWork.AmbientTransaction;
                insert.CommandText = "INSERT INTO TaskTags (TaskId, TagId) VALUES ($t, $g);";
                insert.Parameters.AddWithValue("$t", taskId.ToString());
                insert.Parameters.AddWithValue("$g", tagId.ToString());
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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

    public async Task<IReadOnlyList<TagId>> GetTagIdsForTaskAsync(
        TaskId taskId,
        CancellationToken cancellationToken = default)
    {
        var map = await GetTagIdsForTasksAsync([taskId], cancellationToken).ConfigureAwait(false);
        return map.TryGetValue(taskId, out var ids) ? ids : [];
    }

    public async Task<IReadOnlyDictionary<TaskId, IReadOnlyList<TagId>>> GetTagIdsForTasksAsync(
        IReadOnlyList<TaskId> taskIds,
        CancellationToken cancellationToken = default)
    {
        var result = taskIds.ToDictionary(id => id, _ => (IReadOnlyList<TagId>)Array.Empty<TagId>());
        if (taskIds.Count == 0)
        {
            return result;
        }

        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            var buckets = new Dictionary<TaskId, List<TagId>>();
            foreach (var id in taskIds)
            {
                buckets[id] = [];
            }

            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            var parameters = new List<string>();
            for (var i = 0; i < taskIds.Count; i++)
            {
                var name = $"$t{i}";
                parameters.Add(name);
                cmd.Parameters.AddWithValue(name, taskIds[i].ToString());
            }

            cmd.CommandText = $"SELECT TaskId, TagId FROM TaskTags WHERE TaskId IN ({string.Join(",", parameters)});";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var taskId = TaskId.Parse(Convert.ToString(reader["TaskId"])!);
                var tagId = TagId.Parse(Convert.ToString(reader["TagId"])!);
                if (buckets.TryGetValue(taskId, out var list))
                {
                    list.Add(tagId);
                }
            }

            return buckets.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyList<TagId>)kv.Value);
        }
        finally
        {
            if (owns)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static Tag Map(SqliteDataReader reader)
    {
        string? color = reader["Color"] is DBNull ? null : Convert.ToString(reader["Color"]);
        return new Tag
        {
            Id = TagId.Parse(Convert.ToString(reader["Id"])!),
            Name = Convert.ToString(reader["Name"])!,
            Color = color,
            CreatedUtc = DateTimeOffset.Parse(Convert.ToString(reader["CreatedUtc"])!)
        };
    }
}
