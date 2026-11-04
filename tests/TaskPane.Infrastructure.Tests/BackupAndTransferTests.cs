using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TaskPane.Application.DependencyInjection;
using TaskPane.Application.Models;
using TaskPane.Application.Services;
using TaskPane.Core.Enums;
using TaskPane.Core.Errors;
using TaskPane.Core.Interfaces;
using TaskPane.Core.Transfer;
using TaskPane.Infrastructure.DependencyInjection;
using TaskPane.Infrastructure.Migrations;
using TaskPane.Infrastructure.Paths;
using CreateTaskRequest = TaskPane.Application.Models.CreateTaskRequest;

namespace TaskPane.Infrastructure.Tests;

public sealed class BackupAndTransferTests
{
    [Fact]
    public async Task Backup_uses_sqlite_online_api_and_survives_unavailable_documents()
    {
        var root = TempRoot();
        var dbPath = Path.Combine(root, "tasks.db");
        var fakeDocs = Path.Combine(root, "MissingDocuments");

        try
        {
            await using var services = Build(
                dbPath,
                new DocumentsFolderResolver(
                    () => fakeDocs,
                    Directory.Exists,
                    path => Directory.CreateDirectory(path)));

            await services.GetRequiredService<SqliteMigrator>().MigrateAsync();
            var tasks = services.GetRequiredService<TaskService>();
            await tasks.CreateAsync(new CreateTaskRequest { Title = "Keep me" });

            var backups = services.GetRequiredService<BackupService>();
            var result = await backups.CreateBackupAsync("test");

            Assert.True(File.Exists(result.LocalPath));
            Assert.True(
                result.Outcome is BackupOutcome.SuccessLocalOnly or BackupOutcome.DocumentsUnavailable);
            Assert.DoesNotContain("Documents", result.LocalPath, StringComparison.OrdinalIgnoreCase);

            var verified = await services.GetRequiredService<IDatabaseBackupRunner>()
                .VerifyBackupAsync(result.LocalPath);
            Assert.True(verified);

            var status = await backups.GetStatusAsync();
            Assert.False(status.DocumentsAvailable);
            Assert.Contains("offline", status.StatusText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Backup_copies_to_documents_when_reachable_and_applies_retention()
    {
        var root = TempRoot();
        var dbPath = Path.Combine(root, "tasks.db");
        var docs = Path.Combine(root, "Documents");
        Directory.CreateDirectory(docs);

        try
        {
            await using var services = Build(
                dbPath,
                new DocumentsFolderResolver(
                    () => docs,
                    Directory.Exists,
                    path => Directory.CreateDirectory(path)));

            await services.GetRequiredService<SqliteMigrator>().MigrateAsync();
            var backups = services.GetRequiredService<BackupService>();
            await backups.SaveSettingsAsync(new Core.Entities.BackupSettings
            {
                LocalRetentionCount = 2,
                DocumentsRetentionCount = 2,
                IntervalMinutes = 30
            });

            await services.GetRequiredService<TaskService>()
                .CreateAsync(new CreateTaskRequest { Title = "A" });

            var r1 = await backups.CreateBackupAsync("one");
            await Task.Delay(20);
            var r2 = await backups.CreateBackupAsync("two");
            await Task.Delay(20);
            var r3 = await backups.CreateBackupAsync("three");

            Assert.Equal(BackupOutcome.SuccessWithDocuments, r3.Outcome);
            Assert.True(File.Exists(r3.DocumentsPath));

            var localDir = Path.GetDirectoryName(r3.LocalPath)!;
            var localCount = Directory.GetFiles(localDir, "tasks-backup-*.db").Length;
            Assert.True(localCount <= 2);

            var docsDir = Path.Combine(docs, "TaskPane", "Backups");
            var docsCount = Directory.GetFiles(docsDir, "tasks-backup-*.db").Length;
            Assert.True(docsCount <= 2);
            _ = r1;
            _ = r2;
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Json_export_import_round_trip_preserves_tasks_and_rejects_invalid_payload()
    {
        var root = TempRoot();
        var sourceDb = Path.Combine(root, "source.db");
        var targetDb = Path.Combine(root, "target.db");
        var docs = Path.Combine(root, "Documents");
        Directory.CreateDirectory(docs);

        try
        {
            string json;
            string parentTitle;
            string childTitle;

            await using (var source = Build(sourceDb, AvailableDocs(docs)))
            {
                await source.GetRequiredService<SqliteMigrator>().MigrateAsync();
                var tasks = source.GetRequiredService<TaskService>();
                var org = source.GetRequiredService<OrganizationService>();
                var deps = source.GetRequiredService<DependencyService>();
                var time = source.GetRequiredService<TimeTrackingService>();

                var project = await org.CreateProjectAsync("Alpha");
                var tag = await org.CreateTagAsync("urgent");
                var parent = await tasks.CreateAsync(new CreateTaskRequest
                {
                    Title = "Parent",
                    FocusBucket = FocusBucket.Today,
                    ProjectId = project.Id,
                    Notes = "note body"
                });
                await org.SetTaskTagsAsync(parent.Id, [tag.Id]);
                var child = await tasks.CreateAsync(new CreateTaskRequest
                {
                    Title = "Child",
                    ParentTaskId = parent.Id
                });
                var blocker = await tasks.CreateAsync(new CreateTaskRequest { Title = "Blocker" });
                await deps.AddRequiredAsync(blocker.Id, parent.Id);
                await time.StartOrSwitchAsync(parent.Id);
                await time.StopAsync();

                parentTitle = parent.Title;
                childTitle = child.Title;

                var transfer = source.GetRequiredService<TransferService>();
                var path = await transfer.ExportJsonToFileAsync(Path.Combine(root, "exports"));
                json = await File.ReadAllTextAsync(path);
                Assert.Contains("\"schemaVersion\": 1", json, StringComparison.Ordinal);
                Assert.Contains("\"application\": \"TaskPane\"", json, StringComparison.Ordinal);
            }

            await using (var target = Build(targetDb, AvailableDocs(docs)))
            {
                await target.GetRequiredService<SqliteMigrator>().MigrateAsync();
                var transfer = target.GetRequiredService<TransferService>();

                var bad = await Assert.ThrowsAsync<BusinessException>(() =>
                    transfer.ImportJsonAsync("{ not json", ImportMode.ImportAsNew, dryRun: false));
                Assert.Equal(ValidationCodes.ImportInvalid, bad.Code);

                var cycle = """
                    {
                      "schemaVersion": 1,
                      "application": "TaskPane",
                      "exportedAtUtc": "2026-11-04T10:00:00Z",
                      "tasks": [
                        { "id": "11111111-1111-1111-1111-111111111111", "title": "A", "priority": 1, "focusBucket": 0, "status": 0, "manualSortKey": 1, "createdUtc": "2026-11-04T10:00:00Z", "updatedUtc": "2026-11-04T10:00:00Z", "revision": 1 },
                        { "id": "22222222-2222-2222-2222-222222222222", "title": "B", "priority": 1, "focusBucket": 0, "status": 0, "manualSortKey": 2, "createdUtc": "2026-11-04T10:00:00Z", "updatedUtc": "2026-11-04T10:00:00Z", "revision": 1 }
                      ],
                      "dependencies": [
                        { "id": "33333333-3333-3333-3333-333333333333", "predecessorTaskId": "11111111-1111-1111-1111-111111111111", "successorTaskId": "22222222-2222-2222-2222-222222222222", "kind": 0, "createdUtc": "2026-11-04T10:00:00Z" },
                        { "id": "44444444-4444-4444-4444-444444444444", "predecessorTaskId": "22222222-2222-2222-2222-222222222222", "successorTaskId": "11111111-1111-1111-1111-111111111111", "kind": 0, "createdUtc": "2026-11-04T10:00:00Z" }
                      ]
                    }
                    """;
                var preview = transfer.PreviewImport(cycle);
                Assert.False(preview.IsValid);
                Assert.Contains(preview.Errors, e => e.Contains("cycle", StringComparison.OrdinalIgnoreCase));

                var beforeCount = (await target.GetRequiredService<ITaskRepository>()
                    .ListAsync(includeDeleted: true)).Count;

                var result = await transfer.ImportJsonAsync(json, ImportMode.ImportAsNew, dryRun: false);
                Assert.True(result.ImportedTasks >= 3);
                Assert.NotNull(result.SafetyBackupPath);
                Assert.True(File.Exists(result.SafetyBackupPath));

                var imported = await target.GetRequiredService<ITaskRepository>()
                    .ListAsync(includeDeleted: true);
                Assert.Contains(imported, t => t.Title == parentTitle);
                Assert.Contains(imported, t => t.Title == childTitle);
                Assert.True(imported.Count > beforeCount);

                var md = await transfer.ExportMarkdownToFileAsync(Path.Combine(root, "exports"));
                var mdText = await File.ReadAllTextAsync(md);
                Assert.Contains("Limitations", mdText, StringComparison.OrdinalIgnoreCase);
                Assert.Contains(parentTitle, mdText, StringComparison.Ordinal);
            }
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Restore_from_backup_replaces_live_data_after_safety_backup()
    {
        var root = TempRoot();
        var dbPath = Path.Combine(root, "tasks.db");
        var docs = Path.Combine(root, "Documents");
        Directory.CreateDirectory(docs);

        try
        {
            await using var services = Build(dbPath, AvailableDocs(docs));
            await services.GetRequiredService<SqliteMigrator>().MigrateAsync();
            var tasks = services.GetRequiredService<TaskService>();
            await tasks.CreateAsync(new CreateTaskRequest { Title = "Original" });

            var backups = services.GetRequiredService<BackupService>();
            var snapshot = await backups.CreateBackupAsync("baseline");

            await tasks.CreateAsync(new CreateTaskRequest { Title = "Later change" });
            var mid = await tasks.ListVisibleRootsAsync(null);
            Assert.Equal(2, mid.Count);

            await backups.RestoreFromBackupAsync(snapshot.LocalPath);
            var after = await tasks.ListVisibleRootsAsync(null);
            Assert.Single(after);
            Assert.Equal("Original", after[0].Title);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Merge_by_id_skips_existing_and_fail_mode_rejects_conflicts()
    {
        var root = TempRoot();
        var dbPath = Path.Combine(root, "tasks.db");
        var docs = Path.Combine(root, "Documents");
        Directory.CreateDirectory(docs);

        try
        {
            await using var services = Build(dbPath, AvailableDocs(docs));
            await services.GetRequiredService<SqliteMigrator>().MigrateAsync();
            var tasks = services.GetRequiredService<TaskService>();
            var existing = await tasks.CreateAsync(new CreateTaskRequest { Title = "Existing" });
            var transfer = services.GetRequiredService<TransferService>();

            var doc = new ExportDocument
            {
                SchemaVersion = 1,
                Application = "TaskPane",
                ExportedAtUtc = DateTimeOffset.UtcNow,
                Tasks =
                [
                    new ExportTaskDto
                    {
                        Id = existing.Id.ToString(),
                        Title = "Should skip",
                        Priority = 1,
                        FocusBucket = 0,
                        Status = 0,
                        ManualSortKey = 1,
                        CreatedUtc = DateTimeOffset.UtcNow,
                        UpdatedUtc = DateTimeOffset.UtcNow,
                        Revision = 1
                    },
                    new ExportTaskDto
                    {
                        Id = Guid.NewGuid().ToString("D"),
                        Title = "Fresh",
                        Priority = 1,
                        FocusBucket = 0,
                        Status = 0,
                        ManualSortKey = 2,
                        CreatedUtc = DateTimeOffset.UtcNow,
                        UpdatedUtc = DateTimeOffset.UtcNow,
                        Revision = 1
                    }
                ]
            };
            var json = System.Text.Json.JsonSerializer.Serialize(doc, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });

            var conflict = await Assert.ThrowsAsync<BusinessException>(() =>
                transfer.ImportJsonAsync(json, ImportMode.MergeByIdFailOnConflict, dryRun: false));
            Assert.Equal(ValidationCodes.ImportConflict, conflict.Code);

            var skipped = await transfer.ImportJsonAsync(json, ImportMode.MergeByIdSkipConflicts, dryRun: false);
            Assert.Equal(1, skipped.SkippedTasks);
            Assert.Equal(1, skipped.ImportedTasks);

            var list = await tasks.ListVisibleRootsAsync(null);
            Assert.Contains(list, t => t.Title == "Existing");
            Assert.Contains(list, t => t.Title == "Fresh");
            Assert.DoesNotContain(list, t => t.Title == "Should skip");
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static DocumentsFolderResolver AvailableDocs(string docs)
        => new(() => docs, Directory.Exists, path => Directory.CreateDirectory(path));

    private static ServiceProvider Build(string dbPath, IDocumentsFolderResolver documents)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddTaskPaneInfrastructure(dbPath, documents);
        services.AddTaskPaneApplication();
        return services.BuildServiceProvider();
    }

    private static string TempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "TaskPaneTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDelete(string root)
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}
