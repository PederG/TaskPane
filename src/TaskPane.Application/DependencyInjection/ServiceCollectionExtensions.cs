using Microsoft.Extensions.DependencyInjection;
using TaskPane.Application.Services;

namespace TaskPane.Application.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTaskPaneApplication(this IServiceCollection services)
    {
        services.AddSingleton<TaskService>();
        services.AddSingleton<OverlaySettingsService>();
        return services;
    }
}
