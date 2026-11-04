using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using TaskPane.Core.Ids;
using TaskPane.Core.Interfaces;

namespace TaskPane.Infrastructure.Sqlite;

public sealed class SqliteBackupHistoryRepository : IBackupHistoryRepository
{
    private readonly SqliteUnitOfWork _unitOfWork;

    public SqliteBackupHistoryRepository(SqliteUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task InsertAsync(BackupHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = """
                INSERT INTO BackupHistory (Id, CreatedUtc, LocalPath, DocumentsPath, Result, ErrorCode, Message)
                VALUES ($id, $created, $local, $docs, $result, $code, $msg);
                """;
            cmd.Parameters.AddWithValue("$id", entry.Id.ToString());
            cmd.Parameters.AddWithValue("$created", entry.CreatedUtc.ToString("O"));
            cmd.Parameters.AddWithValue("$local", entry.LocalPath);
            cmd.Parameters.AddWithValue("$docs", (object?)entry.DocumentsPath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$result", (int)entry.Result);
            cmd.Parameters.AddWithValue("$code", (object?)entry.ErrorCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$msg", (object?)entry.Message ?? DBNull.Value);
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

    public async Task<IReadOnlyList<BackupHistoryEntry>> ListRecentAsync(
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = """
                SELECT Id, CreatedUtc, LocalPath, DocumentsPath, Result, ErrorCode, Message
                FROM BackupHistory
                ORDER BY CreatedUtc DESC
                LIMIT $limit;
                """;
            cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
            var list = new List<BackupHistoryEntry>();
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

    public async Task<BackupHistoryEntry?> GetLatestSuccessfulAsync(CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = """
                SELECT Id, CreatedUtc, LocalPath, DocumentsPath, Result, ErrorCode, Message
                FROM BackupHistory
                WHERE Result IN ($local, $docs)
                ORDER BY CreatedUtc DESC
                LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("$local", (int)BackupOutcome.SuccessLocalOnly);
            cmd.Parameters.AddWithValue("$docs", (int)BackupOutcome.SuccessWithDocuments);
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

    private static BackupHistoryEntry Map(Microsoft.Data.Sqlite.SqliteDataReader reader)
    {
        return new BackupHistoryEntry
        {
            Id = BackupHistoryId.Parse(Convert.ToString(reader["Id"])!),
            CreatedUtc = DateTimeOffset.Parse(Convert.ToString(reader["CreatedUtc"])!),
            LocalPath = Convert.ToString(reader["LocalPath"])!,
            DocumentsPath = reader["DocumentsPath"] is DBNull ? null : Convert.ToString(reader["DocumentsPath"]),
            Result = (BackupOutcome)Convert.ToInt32(reader["Result"]),
            ErrorCode = reader["ErrorCode"] is DBNull ? null : Convert.ToString(reader["ErrorCode"]),
            Message = reader["Message"] is DBNull ? null : Convert.ToString(reader["Message"])
        };
    }
}
