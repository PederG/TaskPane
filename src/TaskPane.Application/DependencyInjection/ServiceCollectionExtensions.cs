using Microsoft.Extensions.DependencyInjection;
using TaskPane.Application.Services;

namespace TaskPane.Application.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTaskPaneApplication(this IServiceCollection services)
    {
        services.AddSingleton<FocusService>();
        services.AddSingleton<OrganizationService>();
        services.AddSingleton<HotkeySettingsService>();
        services.AddSingleton<TimeTrackingService>();
        services.AddSingleton<HistoryService>();
        services.AddSingleton<DependencyService>();
        services.AddSingleton<RecurrenceService>();
        services.AddSingleton<ReminderService>();
        services.AddSingleton<BackupService>();
        services.AddSingleton<TransferService>();
        services.AddSingleton<TaskService>();
        services.AddSingleton<OverlaySettingsService>();
        services.AddSingleton<AppSettingsService>();
        return services;
    }
}
