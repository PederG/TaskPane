using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using TaskPane.Infrastructure.Sqlite;

namespace TaskPane.Infrastructure.Migrations;

public sealed class SqliteMigrator
{
    private readonly Sqlite.SqliteConnectionFactory _factory;
    private readonly IReadOnlyList<IMigration> _migrations;
    private readonly ILogger<SqliteMigrator> _logger;

    public SqliteMigrator(
        Sqlite.SqliteConnectionFactory factory,
        IEnumerable<IMigration> migrations,
        ILogger<SqliteMigrator> logger)
    {
        _factory = factory;
        _migrations = migrations.OrderBy(m => m.Version).ToList();
        _logger = logger;
    }

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using (var bootstrap = connection.CreateCommand())
        {
            bootstrap.CommandText = """
                CREATE TABLE IF NOT EXISTS schema_version (
                    Version INTEGER NOT NULL PRIMARY KEY,
                    Name TEXT NOT NULL,
                    AppliedUtc TEXT NOT NULL
                );
                """;
            await bootstrap.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var current = await GetCurrentVersionAsync(connection, cancellationToken).ConfigureAwait(false);

        foreach (var migration in _migrations.Where(m => m.Version > current))
        {
            await using var tx = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            try
            {
                await migration.ApplyAsync(connection, cancellationToken).ConfigureAwait(false);

                await using (var insert = connection.CreateCommand())
                {
                    insert.Transaction = tx;
                    insert.CommandText = """
                        INSERT INTO schema_version (Version, Name, AppliedUtc)
                        VALUES ($v, $n, $t);
                        """;
                    insert.Parameters.AddWithValue("$v", migration.Version);
                    insert.Parameters.AddWithValue("$n", migration.Name);
                    insert.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToString("O"));
                    await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Applied migration {Version}: {Name}", migration.Version, migration.Name);
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
    }

    private static async Task<int> GetCurrentVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM schema_version;";
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result);
    }
}
