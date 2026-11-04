using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TaskPane.Core.Interfaces;
using TaskPane.Infrastructure.Activity;
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
        services.AddSingleton<IMigration, Migration002_Phase2RichEditing>();
        services.AddSingleton<IMigration, Migration003_Phase3TimeTracking>();
        services.AddSingleton<SqliteMigrator>();
        services.AddSingleton<SqliteUnitOfWork>();
        services.AddSingleton<IUnitOfWork>(sp => sp.GetRequiredService<SqliteUnitOfWork>());
        services.AddSingleton<ITaskRepository, SqliteTaskRepository>();
        services.AddSingleton<IProjectRepository, SqliteProjectRepository>();
        services.AddSingleton<ITagRepository, SqliteTagRepository>();
        services.AddSingleton<ISettingsRepository, SqliteSettingsRepository>();
        services.AddSingleton<ITimeSessionRepository, SqliteTimeSessionRepository>();
        services.AddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IWorkstationActivityMonitor, NullWorkstationActivityMonitor>();
        return services;
    }
}
