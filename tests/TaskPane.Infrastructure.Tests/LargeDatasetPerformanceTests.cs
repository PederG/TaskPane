using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TaskPane.Application.DependencyInjection;
using TaskPane.Application.Models;
using TaskPane.Application.Services;
using TaskPane.Core.Enums;
using TaskPane.Infrastructure.DependencyInjection;
using TaskPane.Infrastructure.Migrations;
using TaskPane.Infrastructure.Paths;

namespace TaskPane.Infrastructure.Tests;

public sealed class LargeDatasetPerformanceTests
{
    [Fact]
    public async Task Query_and_search_remain_responsive_with_thousands_of_tasks()
    {
        var root = Path.Combine(Path.GetTempPath(), "TaskPaneTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, AppPaths.DatabaseFileName);

        try
        {
            await using var services = Build(dbPath);
            await services.GetRequiredService<SqliteMigrator>().MigrateAsync();
            var tasks = services.GetRequiredService<TaskService>();

            const int rootCount = 2500;
            const int childCount = 500;
            for (var i = 0; i < rootCount; i++)
            {
                var created = await tasks.CreateAsync(new CreateTaskRequest
                {
                    Title = $"Root {i:D4}",
                    FocusBucket = i % 2 == 0 ? FocusBucket.Today : FocusBucket.Inbox,
                    Notes = i % 50 == 0 ? $"note marker {i}" : null
                });

                if (i < childCount)
                {
                    await tasks.CreateAsync(new CreateTaskRequest
                    {
                        Title = $"Child {i:D4}",
                        ParentTaskId = created.Id
                    });
                }
            }

            var focus = services.GetRequiredService<FocusService>();
            var sw = Stopwatch.StartNew();
            var today = await focus.QueryRootsAsync(new TaskQuery
            {
                FocusBucket = FocusBucket.Today,
                SortMode = TaskSortMode.Manual
            });
            sw.Stop();
            Assert.True(today.Count >= 1000);
            Assert.True(sw.ElapsedMilliseconds < 5000, $"Today query took {sw.ElapsedMilliseconds}ms");

            sw.Restart();
            var search = await focus.QueryRootsAsync(new TaskQuery
            {
                SearchText = "marker 100",
                IncludeSleeping = true
            });
            sw.Stop();
            Assert.NotEmpty(search);
            Assert.True(sw.ElapsedMilliseconds < 5000, $"Search took {sw.ElapsedMilliseconds}ms");
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
        services.AddTaskPaneApplication();
        return services.BuildServiceProvider();
    }
}
