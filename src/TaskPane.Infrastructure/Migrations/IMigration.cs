using Microsoft.Data.Sqlite;

namespace TaskPane.Infrastructure.Migrations;

public interface IMigration
{
    int Version { get; }
    string Name { get; }
    Task ApplyAsync(SqliteConnection connection, CancellationToken cancellationToken = default);
}
