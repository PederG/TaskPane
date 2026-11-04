using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TaskPane.Application.Models;
using TaskPane.Application.Services;
using TaskPane.Core.Enums;
using TaskPane.Core.Interfaces;
using TaskPane.Infrastructure.DependencyInjection;
using TaskPane.Infrastructure.Migrations;
using TaskPane.Infrastructure.Paths;

namespace TaskPane.Infrastructure.Tests;

public sealed class SqlitePersistenceTests
{
    [Fact]
    public async Task Migrates_and_persists_across_reopen()
    {
        var root = Path.Combine(Path.GetTempPath(), "TaskPaneTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, AppPaths.DatabaseFileName);

        try
        {
            await using (var host1 = Build(dbPath))
            {
                await host1.GetRequiredService<SqliteMigrator>().MigrateAsync();
                var tasks = host1.GetRequiredService<TaskService>();
                var parent = await tasks.CreateAsync(new CreateTaskRequest
                {
                    Title = "Persist me",
                    FocusBucket = FocusBucket.Today,
                    Priority = TaskPriority.High
                });
                await tasks.CreateAsync(new CreateTaskRequest
                {
                    Title = "Child",
                    ParentTaskId = parent.Id
                });
                await tasks.ReorderAsync(parent.Id, 42);

                var settings = host1.GetRequiredService<OverlaySettingsService>();
                await settings.SaveAsync(new Core.Entities.OverlayPlacement
                {
                    Left = 120,
                    Top = 140,
                    Width = 400,
                    Height = 500,
                    Opacity = 0.8,
                    Topmost = true
                });
            }

            await using (var host2 = Build(dbPath))
            {
                await host2.GetRequiredService<SqliteMigrator>().MigrateAsync();
                var tasks = host2.GetRequiredService<TaskService>();
                var roots = await tasks.ListVisibleRootsAsync(FocusBucket.Today);
                Assert.Single(roots);
                Assert.Equal("Persist me", roots[0].Title);
                Assert.Equal(42, roots[0].ManualSortKey);
                Assert.Equal(TaskPriority.High, roots[0].Priority);

                var children = await tasks.ListChildrenAsync(roots[0].Id);
                Assert.Single(children);

                var placement = await host2.GetRequiredService<OverlaySettingsService>().GetAsync();
                Assert.Equal(120, placement.Left);
                Assert.Equal(0.8, placement.Opacity);

                Assert.StartsWith(root, dbPath, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Documents", dbPath, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // best effort cleanup
            }
        }
    }

    [Fact]
    public async Task Soft_delete_and_restore_round_trip()
    {
        var root = Path.Combine(Path.GetTempPath(), "TaskPaneTests", Guid.NewGuid().ToString("N"));
        var dbPath = Path.Combine(root, "tasks.db");
        Directory.CreateDirectory(root);

        try
        {
            await using var services = Build(dbPath);
            await services.GetRequiredService<SqliteMigrator>().MigrateAsync();
            var tasks = services.GetRequiredService<TaskService>();
            var parent = await tasks.CreateAsync(new CreateTaskRequest { Title = "Root" });
            await tasks.CreateAsync(new CreateTaskRequest { Title = "Kid", ParentTaskId = parent.Id });
            await tasks.SoftDeleteAsync(parent.Id);

            var deleted = await tasks.ListDeletedAsync();
            Assert.Equal(2, deleted.Count);
            var batch = deleted.First(t => t.Id.Equals(parent.Id)).DeletionBatchId!.Value;
            await tasks.RestoreBatchAsync(batch);
            Assert.Empty(await tasks.ListDeletedAsync());
            Assert.Single(await tasks.ListVisibleRootsAsync(null));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    private static ServiceProvider Build(string dbPath)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddTaskPaneInfrastructure(dbPath);
        services.AddSingleton<TaskService>();
        services.AddSingleton<OverlaySettingsService>();
        return services.BuildServiceProvider();
    }
}
