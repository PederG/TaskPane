using System.Text.Json;
using TaskPane.Core.Entities;
using TaskPane.Core.Interfaces;

namespace TaskPane.Infrastructure.Sqlite;

public sealed class SqliteSettingsRepository : ISettingsRepository
{
    public const string OverlayPlacementKey = "overlay.placement";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly SqliteUnitOfWork _unitOfWork;

    public SqliteSettingsRepository(SqliteUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<OverlayPlacement> GetOverlayPlacementAsync(CancellationToken cancellationToken = default)
    {
        var raw = await GetRawAsync(OverlayPlacementKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new OverlayPlacement();
        }

        try
        {
            return JsonSerializer.Deserialize<OverlayPlacement>(raw, JsonOptions) ?? new OverlayPlacement();
        }
        catch (JsonException)
        {
            return new OverlayPlacement();
        }
    }

    public Task SaveOverlayPlacementAsync(OverlayPlacement placement, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(placement, JsonOptions);
        return SetRawAsync(OverlayPlacementKey, json, cancellationToken);
    }

    public async Task<string?> GetRawAsync(string key, CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = "SELECT JsonValue FROM Settings WHERE Key = $k LIMIT 1;";
            cmd.Parameters.AddWithValue("$k", key);
            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result is null or DBNull ? null : Convert.ToString(result);
        }
        finally
        {
            if (owns)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task SetRawAsync(string key, string jsonValue, CancellationToken cancellationToken = default)
    {
        var connection = await _unitOfWork.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var owns = _unitOfWork.OwnsConnection(connection);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _unitOfWork.AmbientTransaction;
            cmd.CommandText = """
                INSERT INTO Settings (Key, JsonValue) VALUES ($k, $v)
                ON CONFLICT(Key) DO UPDATE SET JsonValue = excluded.JsonValue;
                """;
            cmd.Parameters.AddWithValue("$k", key);
            cmd.Parameters.AddWithValue("$v", jsonValue);
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
}
