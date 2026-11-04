using Microsoft.Data.Sqlite;
using TaskPane.Core.Entities;
using TaskPane.Core.Ids;
using TaskPane.Core.Interfaces;

namespace TaskPane.Infrastructure.Sqlite;

public sealed class SqliteRecurrenceTemplateRepository : IRecurrenceTemplateRepository
{
    private readonly SqliteUnitOfWork _unitOfWork;

    public SqliteRecurrenceTemplateRepository(SqliteUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<RecurrenceTemplate?> GetByIdAsync(
        RecurrenceTemplateId id,
        CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = "SELECT * FROM RecurrenceTemplates WHERE Id = $id LIMIT 1;";
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

    public async Task<IReadOnlyList<RecurrenceTemplate>> ListEnabledAsync(
        CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = "SELECT * FROM RecurrenceTemplates WHERE IsEnabled = 1;";
            var list = new List<RecurrenceTemplate>();
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

    public async Task<IReadOnlyList<RecurrenceTemplate>> ListAllAsync(
        CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = "SELECT * FROM RecurrenceTemplates;";
            var list = new List<RecurrenceTemplate>();
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

    public async Task InsertAsync(RecurrenceTemplate template, CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = """
                INSERT INTO RecurrenceTemplates (
                    Id, SourceTaskId, RuleJson, ContentSnapshotJson, TimeZoneId,
                    NextEvaluationUtc, IsEnabled, LastSkippedOccurrenceKey, CreatedUtc, UpdatedUtc)
                VALUES (
                    $id, $source, $rule, $content, $tz,
                    $next, $enabled, $skipped, $created, $updated);
                """;
            Bind(cmd, template);
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

    public async Task UpdateAsync(RecurrenceTemplate template, CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = """
                UPDATE RecurrenceTemplates SET
                    SourceTaskId = $source,
                    RuleJson = $rule,
                    ContentSnapshotJson = $content,
                    TimeZoneId = $tz,
                    NextEvaluationUtc = $next,
                    IsEnabled = $enabled,
                    LastSkippedOccurrenceKey = $skipped,
                    CreatedUtc = $created,
                    UpdatedUtc = $updated
                WHERE Id = $id;
                """;
            Bind(cmd, template);
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

    private static void Bind(SqliteCommand cmd, RecurrenceTemplate template)
    {
        cmd.Parameters.AddWithValue("$id", template.Id.ToString());
        cmd.Parameters.AddWithValue("$source", template.SourceTaskId.ToString());
        cmd.Parameters.AddWithValue("$rule", template.RuleJson);
        cmd.Parameters.AddWithValue("$content", template.ContentSnapshotJson);
        cmd.Parameters.AddWithValue("$tz", template.TimeZoneId);
        cmd.Parameters.AddWithValue("$next", (object?)template.NextEvaluationUtc?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$enabled", template.IsEnabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$skipped", (object?)template.LastSkippedOccurrenceKey ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", template.CreatedUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$updated", template.UpdatedUtc.ToString("O"));
    }

    private static RecurrenceTemplate Map(SqliteDataReader reader)
    {
        string? next = reader["NextEvaluationUtc"] is DBNull ? null : Convert.ToString(reader["NextEvaluationUtc"]);
        string? skipped = reader["LastSkippedOccurrenceKey"] is DBNull
            ? null
            : Convert.ToString(reader["LastSkippedOccurrenceKey"]);

        return new RecurrenceTemplate
        {
            Id = RecurrenceTemplateId.Parse(Convert.ToString(reader["Id"])!),
            SourceTaskId = TaskId.Parse(Convert.ToString(reader["SourceTaskId"])!),
            RuleJson = Convert.ToString(reader["RuleJson"])!,
            ContentSnapshotJson = Convert.ToString(reader["ContentSnapshotJson"])!,
            TimeZoneId = Convert.ToString(reader["TimeZoneId"])!,
            NextEvaluationUtc = next is null ? null : DateTimeOffset.Parse(next),
            IsEnabled = Convert.ToInt32(reader["IsEnabled"]) != 0,
            LastSkippedOccurrenceKey = skipped,
            CreatedUtc = DateTimeOffset.Parse(Convert.ToString(reader["CreatedUtc"])!),
            UpdatedUtc = DateTimeOffset.Parse(Convert.ToString(reader["UpdatedUtc"])!)
        };
    }
}
