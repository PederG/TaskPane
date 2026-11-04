using TaskPane.Core.Interfaces;

namespace TaskPane.Infrastructure.Sqlite;

public sealed class SqliteUnitOfWork : IUnitOfWork
{
    private readonly SqliteConnectionFactory _factory;
    private readonly AsyncLocal<Microsoft.Data.Sqlite.SqliteConnection?> _ambientConnection = new();
    private readonly AsyncLocal<Microsoft.Data.Sqlite.SqliteTransaction?> _ambientTransaction = new();

    public SqliteUnitOfWork(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    internal Microsoft.Data.Sqlite.SqliteConnection? AmbientConnection => _ambientConnection.Value;
    internal Microsoft.Data.Sqlite.SqliteTransaction? AmbientTransaction => _ambientTransaction.Value;

    public async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        if (_ambientTransaction.Value is not null)
        {
            await action(cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = (Microsoft.Data.Sqlite.SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        _ambientConnection.Value = connection;
        _ambientTransaction.Value = tx;

        try
        {
            await action(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
        finally
        {
            _ambientConnection.Value = null;
            _ambientTransaction.Value = null;
        }
    }

    public async Task<Microsoft.Data.Sqlite.SqliteConnection> GetConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        if (_ambientConnection.Value is { } ambient)
        {
            return ambient;
        }

        return await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
    }

    public bool OwnsConnection(Microsoft.Data.Sqlite.SqliteConnection connection)
        => !ReferenceEquals(connection, _ambientConnection.Value);
}
