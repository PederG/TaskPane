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
        services.AddSingleton<TaskService>();
        services.AddSingleton<OverlaySettingsService>();
        return services;
    }
}
