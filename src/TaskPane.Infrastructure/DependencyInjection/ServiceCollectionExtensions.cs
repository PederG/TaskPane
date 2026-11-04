using Microsoft.Extensions.DependencyInjection;
using TaskPane.Core.Interfaces;
using TaskPane.Infrastructure.Migrations;
using TaskPane.Infrastructure.Paths;
using TaskPane.Infrastructure.Sqlite;

namespace TaskPane.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTaskPaneInfrastructure(
        this IServiceCollection services,
        string? databasePath = null)
    {
        var path = databasePath ?? AppPaths.GetDatabasePath();
        services.AddSingleton(new SqliteConnectionFactory(path));
        services.AddSingleton<IMigration, Migration001_InitialSchema>();
        services.AddSingleton<SqliteMigrator>();
        services.AddSingleton<SqliteUnitOfWork>();
        services.AddSingleton<IUnitOfWork>(sp => sp.GetRequiredService<SqliteUnitOfWork>());
        services.AddSingleton<ITaskRepository, SqliteTaskRepository>();
        services.AddSingleton<ISettingsRepository, SqliteSettingsRepository>();
        return services;
    }
}
