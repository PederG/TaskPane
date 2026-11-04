using Microsoft.Data.Sqlite;
using TaskPane.Core.Entities;
using TaskPane.Core.Ids;
using TaskPane.Core.Interfaces;

namespace TaskPane.Infrastructure.Sqlite;

public sealed class SqliteProjectRepository : IProjectRepository
{
    private readonly SqliteUnitOfWork _unitOfWork;

    public SqliteProjectRepository(SqliteUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Project?> GetByIdAsync(ProjectId id, CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = "SELECT * FROM Projects WHERE Id = $id LIMIT 1;";
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

    public async Task<IReadOnlyList<Project>> ListAsync(
        bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = includeArchived
                ? "SELECT * FROM Projects ORDER BY Name COLLATE NOCASE;"
                : "SELECT * FROM Projects WHERE ArchivedUtc IS NULL ORDER BY Name COLLATE NOCASE;";
            var list = new List<Project>();
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

    public async Task InsertAsync(Project project, CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = """
                INSERT INTO Projects (Id, Name, Color, ArchivedUtc, CreatedUtc, UpdatedUtc)
                VALUES ($id, $name, $color, $archived, $created, $updated);
                """;
            Bind(cmd, project);
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

    public async Task UpdateAsync(Project project, CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = """
                UPDATE Projects SET
                    Name = $name,
                    Color = $color,
                    ArchivedUtc = $archived,
                    CreatedUtc = $created,
                    UpdatedUtc = $updated
                WHERE Id = $id;
                """;
            Bind(cmd, project);
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

    private static void Bind(SqliteCommand cmd, Project project)
    {
        cmd.Parameters.AddWithValue("$id", project.Id.ToString());
        cmd.Parameters.AddWithValue("$name", project.Name);
        cmd.Parameters.AddWithValue("$color", (object?)project.Color ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$archived", (object?)project.ArchivedUtc?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", project.CreatedUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$updated", project.UpdatedUtc.ToString("O"));
    }

    private static Project Map(SqliteDataReader reader)
    {
        string? archived = reader["ArchivedUtc"] is DBNull ? null : Convert.ToString(reader["ArchivedUtc"]);
        string? color = reader["Color"] is DBNull ? null : Convert.ToString(reader["Color"]);
        return new Project
        {
            Id = ProjectId.Parse(Convert.ToString(reader["Id"])!),
            Name = Convert.ToString(reader["Name"])!,
            Color = color,
            ArchivedUtc = archived is null ? null : DateTimeOffset.Parse(archived),
            CreatedUtc = DateTimeOffset.Parse(Convert.ToString(reader["CreatedUtc"])!),
            UpdatedUtc = DateTimeOffset.Parse(Convert.ToString(reader["UpdatedUtc"])!)
        };
    }
}
