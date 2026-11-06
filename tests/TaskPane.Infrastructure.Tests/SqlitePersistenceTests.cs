using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TaskPane.Application.DependencyInjection;
using TaskPane.Application.Models;
using TaskPane.Application.Services;
using TaskPane.Core.Enums;
using TaskPane.Core.Ids;
using TaskPane.Infrastructure.DependencyInjection;
using TaskPane.Infrastructure.Migrations;
using TaskPane.Infrastructure.Paths;
using TaskPane.Infrastructure.Sqlite;
using SessionEndReason = TaskPane.Core.Enums.SessionEndReason;

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
                Assert.True(placement.Topmost);

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
    public async Task Legacy_overlay_opacity_json_is_ignored_and_dropped_on_save()
    {
        // Non-destructive: keep Settings row; ignore unknown "opacity" on load; omit on rewrite.
        var root = Path.Combine(Path.GetTempPath(), "TaskPaneTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, AppPaths.DatabaseFileName);

        try
        {
            await using var services = Build(dbPath);
            await services.GetRequiredService<SqliteMigrator>().MigrateAsync();
            var repo = services.GetRequiredService<Core.Interfaces.ISettingsRepository>();

            await repo.SetRawAsync(
                SqliteSettingsRepository.OverlayPlacementKey,
                """{"left":99,"top":88,"width":360,"height":480,"maxHeight":720,"opacity":0.55,"topmost":true,"positionLocked":false}""");

            var placement = await services.GetRequiredService<OverlaySettingsService>().GetAsync();
            Assert.Equal(99, placement.Left);
            Assert.Equal(88, placement.Top);
            Assert.True(placement.Topmost);

            await services.GetRequiredService<OverlaySettingsService>().SaveAsync(placement);
            var raw = await repo.GetRawAsync(SqliteSettingsRepository.OverlayPlacementKey);
            Assert.NotNull(raw);
            Assert.DoesNotContain("opacity", raw!, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"left\":99", raw!, StringComparison.Ordinal);
            Assert.Contains("\"topmost\":true", raw!, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
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

    [Fact]
    public async Task Phase2_migration_persists_deadline_sleep_tags_and_projects()
    {
        var root = Path.Combine(Path.GetTempPath(), "TaskPaneTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "tasks.db");

        try
        {
            await using var services = Build(dbPath);
            await services.GetRequiredService<SqliteMigrator>().MigrateAsync();
            var org = services.GetRequiredService<OrganizationService>();
            var tasks = services.GetRequiredService<TaskService>();

            var project = await org.CreateProjectAsync("Infra");
            var tag = await org.CreateTagAsync("urgent");
            var created = await tasks.CreateAsync(new CreateTaskRequest
            {
                Title = "Rich task",
                Notes = "markdown **note**",
                FocusBucket = FocusBucket.Today,
                DeadlineLocalDate = "2026-11-10",
                ProjectId = project.Id
            });
            await tasks.UpdateAsync(new UpdateTaskRequest
            {
                Id = created.Id,
                Title = created.Title,
                Notes = created.Notes,
                Priority = TaskPriority.High,
                FocusBucket = FocusBucket.Today,
                DeadlineLocalDate = "2026-11-10",
                ProjectId = project.Id,
                TagIds = [tag.Id],
                WaitingFor = "Change window"
            });
            await tasks.SleepAsync(new SleepTaskRequest
            {
                Id = created.Id,
                Preset = SleepPreset.Indefinitely
            });

            var loaded = await tasks.GetAsync(created.Id);
            Assert.NotNull(loaded);
            Assert.Equal("2026-11-10", loaded!.DeadlineLocalDate);
            Assert.True(loaded.SleepIndefinitely);
            Assert.Equal("Change window", loaded.WaitingFor);
            Assert.Equal(project.Id, loaded.ProjectId);
            Assert.Equal([tag.Id], await org.GetTaskTagIdsAsync(created.Id));
            Assert.Empty(await tasks.ListVisibleRootsAsync(FocusBucket.Today));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Overlay_notes_create_and_edit_persist_across_restart()
    {
        var root = Path.Combine(Path.GetTempPath(), "TaskPaneTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "tasks.db");
        TaskId taskId;

        try
        {
            await using (var host1 = Build(dbPath))
            {
                await host1.GetRequiredService<SqliteMigrator>().MigrateAsync();
                var tasks = host1.GetRequiredService<TaskService>();
                var created = await tasks.CreateAsync(new CreateTaskRequest
                {
                    Title = "Notes only via overlay",
                    FocusBucket = FocusBucket.Inbox
                });
                taskId = created.Id;
                Assert.Null(created.Notes);

                await tasks.UpdateAsync(new UpdateTaskRequest
                {
                    Id = taskId,
                    Title = created.Title,
                    Notes = "created\nfrom overlay",
                    Priority = created.Priority,
                    FocusBucket = created.FocusBucket
                });
            }

            await using (var host2 = Build(dbPath))
            {
                await host2.GetRequiredService<SqliteMigrator>().MigrateAsync();
                var tasks = host2.GetRequiredService<TaskService>();
                var loaded = await tasks.GetAsync(taskId);
                Assert.NotNull(loaded);
                Assert.Equal("created\nfrom overlay", loaded!.Notes);

                await tasks.UpdateAsync(new UpdateTaskRequest
                {
                    Id = taskId,
                    Title = loaded.Title,
                    Notes = "edited later",
                    Priority = loaded.Priority,
                    FocusBucket = loaded.FocusBucket
                });
            }

            await using var host3 = Build(dbPath);
            await host3.GetRequiredService<SqliteMigrator>().MigrateAsync();
            var again = await host3.GetRequiredService<TaskService>().GetAsync(taskId);
            Assert.Equal("edited later", again!.Notes);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Custom_sleep_survives_restart_then_auto_wakes_after_expiry()
    {
        var root = Path.Combine(Path.GetTempPath(), "TaskPaneTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "tasks.db");
        var until = DateTimeOffset.UtcNow.AddHours(3);
        TaskId taskId;

        try
        {
            await using (var host1 = Build(dbPath))
            {
                await host1.GetRequiredService<SqliteMigrator>().MigrateAsync();
                var tasks = host1.GetRequiredService<TaskService>();
                var created = await tasks.CreateAsync(new CreateTaskRequest
                {
                    Title = "Restart nap",
                    Notes = "sticky notes",
                    FocusBucket = FocusBucket.Today,
                    Priority = TaskPriority.Low,
                    DeadlineLocalDate = "2026-12-20"
                });
                taskId = created.Id;
                await tasks.SleepAsync(new SleepTaskRequest
                {
                    Id = taskId,
                    Preset = SleepPreset.Custom,
                    CustomUntilUtc = until
                });
                Assert.Empty(await tasks.ListVisibleRootsAsync(FocusBucket.Today));
            }

            await using (var host2 = Build(dbPath))
            {
                await host2.GetRequiredService<SqliteMigrator>().MigrateAsync();
                var tasks = host2.GetRequiredService<TaskService>();
                var loaded = await tasks.GetAsync(taskId);
                Assert.NotNull(loaded);
                Assert.Equal(until, loaded!.SleepUntilUtc);
                Assert.False(loaded.SleepIndefinitely);
                Assert.Equal("sticky notes", loaded.Notes);
                Assert.Equal(TaskPriority.Low, loaded.Priority);
                Assert.Equal("2026-12-20", loaded.DeadlineLocalDate);
                Assert.Empty(await tasks.ListVisibleRootsAsync(FocusBucket.Today));
                Assert.Contains(await tasks.ListSleepingRootsAsync(), t => t.Id.Equals(taskId));

                // Expire sleep in-place (simulates wall-clock passing across restart).
                loaded.SleepUntilUtc = DateTimeOffset.UtcNow.AddMinutes(-5);
                await host2.GetRequiredService<TaskPane.Core.Interfaces.ITaskRepository>()
                    .UpdateAsync(loaded);

                Assert.Contains(await tasks.ListVisibleRootsAsync(FocusBucket.Today), t => t.Id.Equals(taskId));
                Assert.DoesNotContain(await tasks.ListSleepingRootsAsync(), t => t.Id.Equals(taskId));
                var after = await tasks.GetAsync(taskId);
                Assert.Equal("sticky notes", after!.Notes);
                Assert.Equal("2026-12-20", after.DeadlineLocalDate);
                Assert.Equal(TaskPriority.Low, after.Priority);
            }
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Phase3_migration_enforces_one_open_session_and_persists_switch()
    {
        var root = Path.Combine(Path.GetTempPath(), "TaskPaneTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "tasks.db");

        try
        {
            await using (var services = Build(dbPath))
            {
                await services.GetRequiredService<SqliteMigrator>().MigrateAsync();
                var tasks = services.GetRequiredService<TaskService>();
                var time = services.GetRequiredService<TimeTrackingService>();
                var a = await tasks.CreateAsync(new CreateTaskRequest { Title = "A" });
                var b = await tasks.CreateAsync(new CreateTaskRequest { Title = "B" });
                await time.StartOrSwitchAsync(a.Id);
                await time.StartOrSwitchAsync(b.Id);
                await time.StopAsync();

                var open = await time.GetActiveSessionAsync();
                Assert.Null(open);
            }

            await using (var services = Build(dbPath))
            {
                await services.GetRequiredService<SqliteMigrator>().MigrateAsync();
                var tasks = services.GetRequiredService<TaskService>();
                var time = services.GetRequiredService<TimeTrackingService>();
                var roots = await tasks.ListVisibleRootsAsync(null);
                var a = roots.Single(t => t.Title == "A");
                var b = roots.Single(t => t.Title == "B");
                Assert.Single(await time.ListSessionsForTaskAsync(a.Id));
                Assert.Single(await time.ListSessionsForTaskAsync(b.Id));
                Assert.Equal(SessionEndReason.Switched, (await time.ListSessionsForTaskAsync(a.Id))[0].EndReason);
                Assert.Equal(SessionEndReason.Stopped, (await time.ListSessionsForTaskAsync(b.Id))[0].EndReason);
            }
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Phase4_dependencies_history_and_undo_persist()
    {
        var root = Path.Combine(Path.GetTempPath(), "TaskPaneTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "tasks.db");

        try
        {
            await using (var services = Build(dbPath))
            {
                await services.GetRequiredService<SqliteMigrator>().MigrateAsync();
                var tasks = services.GetRequiredService<TaskService>();
                var deps = services.GetRequiredService<DependencyService>();
                var history = services.GetRequiredService<HistoryService>();

                var blocker = await tasks.CreateAsync(new CreateTaskRequest { Title = "Blocker" });
                var dependent = await tasks.CreateAsync(new CreateTaskRequest { Title = "Dependent" });
                await deps.AddRequiredAsync(blocker.Id, dependent.Id);

                var cycle = await Assert.ThrowsAsync<Core.Errors.BusinessException>(() =>
                    deps.AddRequiredAsync(dependent.Id, blocker.Id));
                Assert.Equal(Core.Errors.ValidationCodes.DependencyCycle, cycle.Code);

                Assert.True((await deps.GetBlockedStateAsync(dependent.Id)).IsBlocked);
                await tasks.CompleteAsync(blocker.Id, null);
                Assert.False((await deps.GetBlockedStateAsync(dependent.Id)).IsBlocked);

                await history.UndoLatestAsync();
                Assert.False((await tasks.GetAsync(blocker.Id))!.IsCompleted);
                Assert.True((await deps.GetBlockedStateAsync(dependent.Id)).IsBlocked);
            }

            await using (var services = Build(dbPath))
            {
                await services.GetRequiredService<SqliteMigrator>().MigrateAsync();
                var tasks = services.GetRequiredService<TaskService>();
                var deps = services.GetRequiredService<DependencyService>();
                var history = services.GetRequiredService<HistoryService>();
                var roots = await tasks.ListVisibleRootsAsync(null);
                var blocker = roots.Single(t => t.Title == "Blocker");
                var dependent = roots.Single(t => t.Title == "Dependent");
                var info = await deps.GetTaskDependencyInfoAsync(dependent.Id);
                Assert.Single(info.DependsOn);
                Assert.True(info.Blocked.IsBlocked);
                Assert.Contains(await history.ListRecentAsync(), e => e.Action == Core.Enums.AuditAction.Completed && e.IsUndone);
            }
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Expanded_field_and_dependency_undo_survives_restart()
    {
        var root = Path.Combine(Path.GetTempPath(), "TaskPaneTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "tasks.db");
        TaskId taskId;
        TaskId blockerId;
        TaskId dependentId;

        try
        {
            await using (var services = Build(dbPath))
            {
                await services.GetRequiredService<SqliteMigrator>().MigrateAsync();
                var tasks = services.GetRequiredService<TaskService>();
                var org = services.GetRequiredService<OrganizationService>();
                var deps = services.GetRequiredService<DependencyService>();
                var history = services.GetRequiredService<HistoryService>();

                var project = await org.CreateProjectAsync("RestartProj");
                var tag = await org.CreateTagAsync("restart-tag");
                var created = await tasks.CreateAsync(new CreateTaskRequest
                {
                    Title = "Before",
                    Notes = "old",
                    Priority = TaskPriority.Low,
                    FocusBucket = FocusBucket.Inbox
                });
                taskId = created.Id;

                await tasks.UpdateAsync(new UpdateTaskRequest
                {
                    Id = taskId,
                    Title = "After",
                    Notes = "new notes",
                    Priority = TaskPriority.High,
                    FocusBucket = FocusBucket.Today,
                    DeadlineLocalDate = "2026-12-25",
                    DeadlineLocalTime = "14:00",
                    ProjectId = project.Id,
                    TagIds = [tag.Id]
                });

                var blocker = await tasks.CreateAsync(new CreateTaskRequest { Title = "BlockerR" });
                var dependent = await tasks.CreateAsync(new CreateTaskRequest { Title = "DependentR" });
                blockerId = blocker.Id;
                dependentId = dependent.Id;
                await deps.AddRequiredAsync(blockerId, dependentId);

                Assert.True((await history.EvaluateUndoAsync((await history.PeekUndoAsync())!.Id)).CanUndo);
            }

            await using (var services = Build(dbPath))
            {
                await services.GetRequiredService<SqliteMigrator>().MigrateAsync();
                var tasks = services.GetRequiredService<TaskService>();
                var deps = services.GetRequiredService<DependencyService>();
                var history = services.GetRequiredService<HistoryService>();
                var tags = services.GetRequiredService<TaskPane.Core.Interfaces.ITagRepository>();

                // Undo dependency add first (latest), then the field update.
                await history.UndoLatestAsync();
                Assert.False((await deps.GetBlockedStateAsync(dependentId)).IsBlocked);

                await history.UndoLatestAsync();
                var restored = await tasks.GetAsync(taskId);
                Assert.Equal("Before", restored!.Title);
                Assert.Equal("old", restored.Notes);
                Assert.Equal(TaskPriority.Low, restored.Priority);
                Assert.Equal(FocusBucket.Inbox, restored.FocusBucket);
                Assert.Null(restored.DeadlineLocalDate);
                Assert.Null(restored.ProjectId);
                Assert.Empty(await tags.GetTagIdsForTaskAsync(taskId));

                var events = await history.ListRecentAsync();
                Assert.Contains(events, e => e.Action == Core.Enums.AuditAction.Updated && e.IsUndone);
                Assert.Contains(events, e => e.Action == Core.Enums.AuditAction.DependencyAdded && e.IsUndone);

                var repeatId = events.First(e => e.Action == Core.Enums.AuditAction.Updated && e.IsUndone).Id;
                var repeat = await Assert.ThrowsAsync<Core.Errors.BusinessException>(() => history.UndoAsync(repeatId));
                Assert.Equal(Core.Errors.ValidationCodes.UndoAlreadyApplied, repeat.Code);
            }
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Phase5_recurrence_and_reminders_persist_with_dedupe()
    {
        var root = Path.Combine(Path.GetTempPath(), "TaskPaneTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "tasks.db");

        try
        {
            RecurrenceTemplateId templateId;
            await using (var services = Build(dbPath))
            {
                await services.GetRequiredService<SqliteMigrator>().MigrateAsync();
                var tasks = services.GetRequiredService<TaskService>();
                var recurrence = services.GetRequiredService<RecurrenceService>();
                var reminders = services.GetRequiredService<ReminderService>();

                var seed = await tasks.CreateAsync(new CreateTaskRequest { Title = "Monthly" });
                var template = await recurrence.AttachAsync(new AttachRecurrenceRequest
                {
                    TaskId = seed.Id,
                    Rule = new Core.Recurrence.RecurrenceRule
                    {
                        Frequency = RecurrenceFrequency.Monthly,
                        MonthlyMode = MonthlyRecurrenceMode.DayOfMonth,
                        DayOfMonth = 4,
                        Interval = 1,
                        AnchorLocalDate = "2026-11-04"
                    },
                    TimeZoneId = TimeZoneInfo.Utc.Id,
                    ConvertSourceToFirstOccurrence = true
                });
                templateId = template.Id;

                await tasks.CompleteAsync(seed.Id, ParentCompletionChoice.CompleteParentOnly);
                await reminders.ScheduleAsync(seed.Id, DateTimeOffset.Parse("2026-11-04T15:00:00Z"));
                await reminders.SaveSettingsAsync(new Core.Entities.ReminderSettings
                {
                    QuietHoursEnabled = true,
                    QuietStartLocal = "22:00",
                    QuietEndLocal = "07:00"
                });
            }

            await using (var services = Build(dbPath))
            {
                await services.GetRequiredService<SqliteMigrator>().MigrateAsync();
                var tasks = services.GetRequiredService<TaskService>();
                var recurrence = services.GetRequiredService<RecurrenceService>();
                var reminders = services.GetRequiredService<ReminderService>();
                var taskRepo = services.GetRequiredService<Core.Interfaces.ITaskRepository>();

                var nov = await taskRepo.FindByOccurrenceKeyAsync(templateId, "2026-11-04");
                var dec = await taskRepo.FindByOccurrenceKeyAsync(templateId, "2026-12-04");
                Assert.NotNull(nov);
                Assert.True(nov!.IsCompleted);
                Assert.NotNull(dec);
                Assert.False(dec!.IsCompleted);

                var again = await recurrence.EvaluateTemplateAsync(templateId);
                Assert.Equal(dec.Id, again!.Id);

                var listed = await reminders.ListForTaskAsync(nov.Id);
                Assert.Single(listed);
                var settings = await reminders.GetSettingsAsync();
                Assert.Equal("22:00", settings.QuietStartLocal);
            }
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
