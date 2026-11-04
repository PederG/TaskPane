using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TaskPane.Application.DependencyInjection;
using TaskPane.Application.Services;
using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using TaskPane.Core.Interfaces;
using TaskPane.Infrastructure.DependencyInjection;
using TaskPane.Infrastructure.Migrations;
using TaskPane.Infrastructure.Paths;

namespace TaskPane.Infrastructure.Tests;

public sealed class AppSettingsAndCrashRecoveryTests
{
    [Fact]
    public async Task App_settings_round_trip_and_normalize_scale()
    {
        var root = Path.Combine(Path.GetTempPath(), "TaskPaneTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, AppPaths.DatabaseFileName);

        try
        {
            await using var services = Build(dbPath);
            await services.GetRequiredService<SqliteMigrator>().MigrateAsync();
            var appSettings = services.GetRequiredService<AppSettingsService>();

            await appSettings.SaveAsync(new AppSettings
            {
                AutostartEnabled = true,
                Theme = ThemePreference.Light,
                UiTextScalePercent = 999,
                EnableFileLogging = false
            });

            var loaded = await appSettings.GetAsync();
            Assert.True(loaded.AutostartEnabled);
            Assert.Equal(ThemePreference.Light, loaded.Theme);
            Assert.Equal(150, loaded.UiTextScalePercent);
            Assert.False(loaded.EnableFileLogging);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Crash_marker_detects_unclean_exit()
    {
        var root = Path.Combine(Path.GetTempPath(), "TaskPaneTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var first = new CrashMarkerStore(root);
            Assert.False(first.BeginSession());

            var second = new CrashMarkerStore(root);
            Assert.True(second.BeginSession());
            Assert.True(second.HasUncleanMarker);

            second.EndSessionCleanly();
            var third = new CrashMarkerStore(root);
            Assert.False(third.BeginSession());
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Local_data_guard_requires_writable_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "TaskPaneTests", Guid.NewGuid().ToString("N"));
        var probe = LocalDataGuard.EnsureWritableDataRoot(root);
        Assert.True(probe.Ok);
        Assert.Equal(Path.Combine(root, AppPaths.ProductFolderName), probe.DataDirectory);
        Assert.True(Directory.Exists(AppPaths.GetLogDirectory(root)));
    }

    private static ServiceProvider Build(string dbPath)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddTaskPaneInfrastructure(dbPath);
        services.AddTaskPaneApplication();
        return services.BuildServiceProvider();
    }
}
