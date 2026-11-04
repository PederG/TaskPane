using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TaskPane.Core.Interfaces;
using TaskPane.Infrastructure.Activity;
using TaskPane.Infrastructure.Autostart;
using TaskPane.Infrastructure.Migrations;
using TaskPane.Infrastructure.Notifications;
using TaskPane.Infrastructure.Paths;
using TaskPane.Infrastructure.Sqlite;

namespace TaskPane.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTaskPaneInfrastructure(
        this IServiceCollection services,
        string? databasePath = null,
        IDocumentsFolderResolver? documentsFolderResolver = null)
    {
        var path = databasePath ?? AppPaths.GetDatabasePath();
        services.AddSingleton(new SqliteConnectionFactory(path));
        services.AddSingleton<IMigration, Migration001_InitialSchema>();
        services.AddSingleton<IMigration, Migration002_Phase2RichEditing>();
        services.AddSingleton<IMigration, Migration003_Phase3TimeTracking>();
        services.AddSingleton<IMigration, Migration004_Phase4DependenciesHistory>();
        services.AddSingleton<IMigration, Migration005_Phase5RecurrenceReminders>();
        services.AddSingleton<IMigration, Migration006_Phase6BackupTransfer>();
        services.AddSingleton<SqliteMigrator>();
        services.AddSingleton<SqliteUnitOfWork>();
        services.AddSingleton<IUnitOfWork>(sp => sp.GetRequiredService<SqliteUnitOfWork>());
        services.AddSingleton<ITaskRepository, SqliteTaskRepository>();
        services.AddSingleton<IProjectRepository, SqliteProjectRepository>();
        services.AddSingleton<ITagRepository, SqliteTagRepository>();
        services.AddSingleton<ISettingsRepository, SqliteSettingsRepository>();
        services.AddSingleton<ITimeSessionRepository, SqliteTimeSessionRepository>();
        services.AddSingleton<IDependencyRepository, SqliteDependencyRepository>();
        services.AddSingleton<IAuditRepository, SqliteAuditRepository>();
        services.AddSingleton<IRecurrenceTemplateRepository, SqliteRecurrenceTemplateRepository>();
        services.AddSingleton<IReminderRepository, SqliteReminderRepository>();
        services.AddSingleton<IBackupHistoryRepository, SqliteBackupHistoryRepository>();
        services.AddSingleton<IDatabaseBackupRunner, SqliteDatabaseBackupRunner>();
        if (documentsFolderResolver is null)
        {
            services.TryAddSingleton<IDocumentsFolderResolver, DocumentsFolderResolver>();
        }
        else
        {
            services.AddSingleton(documentsFolderResolver);
        }

        services.AddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IWorkstationActivityMonitor, NullWorkstationActivityMonitor>();
        services.TryAddSingleton<INotificationPresenter, NullNotificationPresenter>();
        services.TryAddSingleton<IAutostartService, NullAutostartService>();
        services.TryAddSingleton<ICrashMarkerStore>(sp =>
        {
            var factory = sp.GetService<SqliteConnectionFactory>();
            var dataDir = factory is null
                ? AppPaths.GetDataDirectory()
                : Path.GetDirectoryName(factory.DatabasePath) ?? AppPaths.GetDataDirectory();
            return new CrashMarkerStore(dataDir);
        });
        return services;
    }
}
