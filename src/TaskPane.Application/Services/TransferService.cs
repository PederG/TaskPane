using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using TaskPane.Application.Models;
using TaskPane.Core.Dependencies;
using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using TaskPane.Core.Errors;
using TaskPane.Core.Ids;
using TaskPane.Core.Interfaces;
using TaskPane.Core.Transfer;
using TaskPane.Core.Validation;

namespace TaskPane.Application.Services;

public sealed class TransferService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly ITaskRepository _tasks;
    private readonly IProjectRepository _projects;
    private readonly ITagRepository _tags;
    private readonly IDependencyRepository _dependencies;
    private readonly ITimeSessionRepository _sessions;
    private readonly IRecurrenceTemplateRepository _recurrence;
    private readonly IReminderRepository _reminders;
    private readonly IAuditRepository _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly BackupService _backups;
    private readonly IDocumentsFolderResolver _documents;
    private readonly IDatabaseBackupRunner _backupRunner;
    private readonly ILogger<TransferService> _logger;

    public TransferService(
        ITaskRepository tasks,
        IProjectRepository projects,
        ITagRepository tags,
        IDependencyRepository dependencies,
        ITimeSessionRepository sessions,
        IRecurrenceTemplateRepository recurrence,
        IReminderRepository reminders,
        IAuditRepository audit,
        IUnitOfWork unitOfWork,
        IClock clock,
        BackupService backups,
        IDocumentsFolderResolver documents,
        IDatabaseBackupRunner backupRunner,
        ILogger<TransferService> logger)
    {
        _tasks = tasks;
        _projects = projects;
        _tags = tags;
        _dependencies = dependencies;
        _sessions = sessions;
        _recurrence = recurrence;
        _reminders = reminders;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _backups = backups;
        _documents = documents;
        _backupRunner = backupRunner;
        _logger = logger;
    }

    public async Task<ExportDocument> BuildExportDocumentAsync(CancellationToken cancellationToken = default)
    {
        var tasks = await _tasks.ListAsync(includeDeleted: true, cancellationToken).ConfigureAwait(false);
        var projects = await _projects.ListAsync(includeArchived: true, cancellationToken).ConfigureAwait(false);
        var tags = await _tags.ListAsync(cancellationToken).ConfigureAwait(false);
        var taskTags = await _tags.ListAllTaskTagsAsync(cancellationToken).ConfigureAwait(false);
        var deps = await _dependencies.ListAsync(includeDeleted: true, cancellationToken).ConfigureAwait(false);
        var sessions = await _sessions.ListAllAsync(includeDiscarded: true, cancellationToken).ConfigureAwait(false);
        var templates = await _recurrence.ListAllAsync(cancellationToken).ConfigureAwait(false);
        var reminders = await _reminders.ListAllAsync(cancellationToken).ConfigureAwait(false);
        var audits = await _audit.ListAllAsync(cancellationToken).ConfigureAwait(false);

        return new ExportDocument
        {
            SchemaVersion = ExportDocument.CurrentSchemaVersion,
            Application = ExportDocument.ApplicationName,
            ExportedAtUtc = _clock.UtcNow,
            Tasks = tasks.Select(MapTask).ToList(),
            Projects = projects.Select(MapProject).ToList(),
            Tags = tags.Select(MapTag).ToList(),
            TaskTags = taskTags.Select(tt => new ExportTaskTagDto
            {
                TaskId = tt.TaskId.ToString(),
                TagId = tt.TagId.ToString()
            }).ToList(),
            Dependencies = deps.Select(MapDependency).ToList(),
            TimeSessions = sessions.Select(MapSession).ToList(),
            RecurrenceTemplates = templates.Select(MapTemplate).ToList(),
            Reminders = reminders.Select(MapReminder).ToList(),
            AuditEvents = audits.Select(MapAudit).ToList()
        };
    }

    public async Task<string> ExportJsonToFileAsync(
        string? preferredDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var document = await BuildExportDocumentAsync(cancellationToken).ConfigureAwait(false);
        var json = JsonSerializer.Serialize(document, JsonOptions);
        var path = ResolveExportPath(preferredDirectory, "taskpane-export", ".json");
        await File.WriteAllTextAsync(path, json, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        return path;
    }

    /// <summary>
    /// Human-readable Markdown export. Not lossless — timers, dependency IDs, recurrence internals,
    /// and audit history are summarized or omitted. Use JSON for round-trip restore.
    /// </summary>
    public async Task<string> ExportMarkdownToFileAsync(
        string? preferredDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var document = await BuildExportDocumentAsync(cancellationToken).ConfigureAwait(false);
        var sb = new StringBuilder();
        sb.AppendLine("# TaskPane export");
        sb.AppendLine();
        sb.AppendLine($"> Generated {_clock.UtcNow:u} (UTC).");
        sb.AppendLine(">");
        sb.AppendLine("> **Limitations:** Markdown is presentation-only. It does not restore timers,");
        sb.AppendLine("> dependency graph IDs, recurrence templates, reminders, soft-delete batches,");
        sb.AppendLine("> or audit history. Use the JSON export for a full round trip.");
        sb.AppendLine();

        var tasksById = document.Tasks.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
        var roots = document.Tasks
            .Where(t => t.ParentTaskId is null && t.DeletedUtc is null)
            .OrderBy(t => t.ManualSortKey)
            .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            AppendMarkdownTask(sb, root, depth: 0);
            foreach (var child in document.Tasks
                         .Where(t => t.ParentTaskId == root.Id && t.DeletedUtc is null)
                         .OrderBy(t => t.ManualSortKey))
            {
                AppendMarkdownTask(sb, child, depth: 1);
            }
        }

        var deleted = document.Tasks.Count(t => t.DeletedUtc is not null);
        if (deleted > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"_Soft-deleted tasks omitted from the readable list: {deleted}._");
        }

        _ = tasksById; // reserved for future cross-link rendering
        var path = ResolveExportPath(preferredDirectory, "taskpane-export", ".md");
        await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        return path;
    }

    public ImportPreview PreviewImport(string json)
    {
        var document = ParseDocument(json);
        return BuildPreview(document);
    }

    public async Task<ImportPreview> PreviewImportFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
        return PreviewImport(json);
    }

    public async Task<ImportResult> ImportJsonAsync(
        string json,
        ImportMode mode,
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
        var preview = PreviewImport(json);
        if (!preview.IsValid)
        {
            throw new BusinessException(
                ValidationCodes.ImportInvalid,
                string.Join(" ", preview.Errors));
        }

        if (dryRun)
        {
            return new ImportResult
            {
                Mode = mode,
                Notes = ["Dry run only — no database changes."]
            };
        }

        var safety = await _backups.CreateBackupAsync("pre-import", cancellationToken).ConfigureAwait(false);
        if (safety.Outcome == BackupOutcome.Failed)
        {
            throw new BusinessException(
                ValidationCodes.ImportInvalid,
                "Refusing import because the pre-import safety backup failed.");
        }

        var document = preview.Document;
        var result = await ApplyImportAsync(document, mode, cancellationToken).ConfigureAwait(false);
        result = new ImportResult
        {
            Mode = result.Mode,
            ImportedTasks = result.ImportedTasks,
            SkippedTasks = result.SkippedTasks,
            ImportedDependencies = result.ImportedDependencies,
            ImportedSessions = result.ImportedSessions,
            ImportedProjects = result.ImportedProjects,
            ImportedTags = result.ImportedTags,
            ImportedReminders = result.ImportedReminders,
            ImportedRecurrenceTemplates = result.ImportedRecurrenceTemplates,
            ImportedAuditEvents = result.ImportedAuditEvents,
            SafetyBackupPath = safety.LocalPath,
            Notes = result.Notes
        };
        return result;
    }

    public async Task<ImportResult> ImportJsonFileAsync(
        string filePath,
        ImportMode mode,
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
        return await ImportJsonAsync(json, mode, dryRun, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ImportResult> ApplyImportAsync(
        ExportDocument document,
        ImportMode mode,
        CancellationToken cancellationToken)
    {
        var existingTasks = (await _tasks.ListAsync(includeDeleted: true, cancellationToken).ConfigureAwait(false))
            .Select(t => t.Id.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingProjects = (await _projects.ListAsync(includeArchived: true, cancellationToken).ConfigureAwait(false))
            .Select(p => p.Id.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingTags = (await _tags.ListAsync(cancellationToken).ConfigureAwait(false))
            .Select(t => t.Id.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingDeps = (await _dependencies.ListAsync(includeDeleted: true, cancellationToken).ConfigureAwait(false))
            .Select(d => d.Id.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingSessions = (await _sessions.ListAllAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
            .Select(s => s.Id.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingTemplates = (await _recurrence.ListAllAsync(cancellationToken).ConfigureAwait(false))
            .Select(t => t.Id.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingReminders = (await _reminders.ListAllAsync(cancellationToken).ConfigureAwait(false))
            .Select(r => r.Id.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingAudits = (await _audit.ListAllAsync(cancellationToken).ConfigureAwait(false))
            .Select(a => a.Id.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var openSession = await _sessions.GetOpenSessionAsync(cancellationToken).ConfigureAwait(false);

        if (mode == ImportMode.MergeByIdFailOnConflict)
        {
            var conflicts = document.Tasks.Where(t => existingTasks.Contains(t.Id)).Select(t => t.Id)
                .Concat(document.Projects.Where(p => existingProjects.Contains(p.Id)).Select(p => p.Id))
                .Concat(document.Tags.Where(t => existingTags.Contains(t.Id)).Select(t => t.Id))
                .Take(5)
                .ToList();
            if (conflicts.Count > 0)
            {
                throw new BusinessException(
                    ValidationCodes.ImportConflict,
                    $"Import aborted: existing IDs conflict ({string.Join(", ", conflicts)}).");
            }
        }

        var idMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string MapId(string? original, Func<string> newId)
        {
            if (string.IsNullOrWhiteSpace(original))
            {
                return newId();
            }

            if (mode == ImportMode.ImportAsNew)
            {
                if (!idMap.TryGetValue(original, out var mapped))
                {
                    mapped = newId();
                    idMap[original] = mapped;
                }

                return mapped;
            }

            return original;
        }

        // Pre-seed maps for ImportAsNew so cross-references resolve consistently.
        if (mode == ImportMode.ImportAsNew)
        {
            foreach (var p in document.Projects)
            {
                MapId(p.Id, () => ProjectId.New().ToString());
            }

            foreach (var t in document.Tags)
            {
                MapId(t.Id, () => TagId.New().ToString());
            }

            foreach (var t in document.Tasks)
            {
                MapId(t.Id, () => TaskId.New().ToString());
            }

            foreach (var d in document.Dependencies)
            {
                MapId(d.Id, () => DependencyId.New().ToString());
            }

            foreach (var s in document.TimeSessions)
            {
                MapId(s.Id, () => SessionId.New().ToString());
            }

            foreach (var r in document.RecurrenceTemplates)
            {
                MapId(r.Id, () => RecurrenceTemplateId.New().ToString());
            }

            foreach (var r in document.Reminders)
            {
                MapId(r.Id, () => ReminderId.New().ToString());
            }

            foreach (var a in document.AuditEvents)
            {
                MapId(a.Id, () => AuditEventId.New().ToString());
            }
        }

        var importedTasks = 0;
        var skippedTasks = 0;
        var importedProjects = 0;
        var importedTags = 0;
        var importedDeps = 0;
        var importedSessions = 0;
        var importedTemplates = 0;
        var importedReminders = 0;
        var importedAudits = 0;
        var notes = new List<string>();

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            foreach (var project in document.Projects)
            {
                var id = MapId(project.Id, () => ProjectId.New().ToString());
                if (mode != ImportMode.ImportAsNew && existingProjects.Contains(id))
                {
                    continue;
                }

                await _projects.InsertAsync(new Project
                {
                    Id = ProjectId.Parse(id),
                    Name = project.Name.Trim(),
                    Color = project.Color,
                    ArchivedUtc = project.ArchivedUtc,
                    CreatedUtc = project.CreatedUtc,
                    UpdatedUtc = project.UpdatedUtc
                }, ct).ConfigureAwait(false);
                importedProjects++;
            }

            foreach (var tag in document.Tags)
            {
                var id = MapId(tag.Id, () => TagId.New().ToString());
                if (mode != ImportMode.ImportAsNew && existingTags.Contains(id))
                {
                    continue;
                }

                // Name collisions: keep imported ID but skip if case-insensitive name exists when merging.
                if (mode != ImportMode.ImportAsNew)
                {
                    var byName = await _tags.FindByNameAsync(tag.Name, ct).ConfigureAwait(false);
                    if (byName is not null && !byName.Id.ToString().Equals(id, StringComparison.OrdinalIgnoreCase))
                    {
                        notes.Add($"Skipped tag '{tag.Name}' due to name collision.");
                        continue;
                    }
                }

                await _tags.InsertAsync(new Tag
                {
                    Id = TagId.Parse(id),
                    Name = tag.Name.Trim(),
                    Color = tag.Color,
                    CreatedUtc = tag.CreatedUtc
                }, ct).ConfigureAwait(false);
                importedTags++;
            }

            // Parents before children.
            var orderedTasks = document.Tasks
                .OrderBy(t => t.ParentTaskId is null ? 0 : 1)
                .ThenBy(t => t.ManualSortKey)
                .ToList();

            foreach (var task in orderedTasks)
            {
                var id = MapId(task.Id, () => TaskId.New().ToString());
                if (mode != ImportMode.ImportAsNew && existingTasks.Contains(id))
                {
                    skippedTasks++;
                    continue;
                }

                string? parentId = null;
                if (!string.IsNullOrWhiteSpace(task.ParentTaskId))
                {
                    parentId = mode == ImportMode.ImportAsNew
                        ? MapId(task.ParentTaskId, () => TaskId.New().ToString())
                        : task.ParentTaskId;
                }

                string? projectId = null;
                if (!string.IsNullOrWhiteSpace(task.ProjectId))
                {
                    projectId = mode == ImportMode.ImportAsNew
                        ? MapId(task.ProjectId, () => ProjectId.New().ToString())
                        : task.ProjectId;
                }

                string? recurrenceId = null;
                if (!string.IsNullOrWhiteSpace(task.RecurrenceTemplateId))
                {
                    recurrenceId = mode == ImportMode.ImportAsNew
                        ? MapId(task.RecurrenceTemplateId, () => RecurrenceTemplateId.New().ToString())
                        : task.RecurrenceTemplateId;
                }

                await _tasks.InsertAsync(new TaskItem
                {
                    Id = TaskId.Parse(id),
                    ParentTaskId = parentId is null ? null : TaskId.Parse(parentId),
                    Title = task.Title.Trim(),
                    Notes = task.Notes,
                    Priority = (TaskPriority)task.Priority,
                    FocusBucket = (FocusBucket)task.FocusBucket,
                    Status = (TaskLifecycleStatus)task.Status,
                    ManualSortKey = task.ManualSortKey,
                    DeadlineLocalDate = task.DeadlineLocalDate,
                    DeadlineLocalTime = task.DeadlineLocalTime,
                    DeadlineTimeZoneId = task.DeadlineTimeZoneId,
                    SleepUntilUtc = task.SleepUntilUtc,
                    SleepIndefinitely = task.SleepIndefinitely,
                    WaitingFor = task.WaitingFor,
                    WaitingFollowUpUtc = task.WaitingFollowUpUtc,
                    ProjectId = projectId is null ? null : ProjectId.Parse(projectId),
                    RecurrenceTemplateId = recurrenceId is null ? null : RecurrenceTemplateId.Parse(recurrenceId),
                    OccurrenceKey = task.OccurrenceKey,
                    CreatedUtc = task.CreatedUtc,
                    UpdatedUtc = task.UpdatedUtc,
                    CompletedUtc = task.CompletedUtc,
                    DeletedUtc = task.DeletedUtc,
                    DeletionBatchId = string.IsNullOrWhiteSpace(task.DeletionBatchId)
                        ? null
                        : Guid.Parse(task.DeletionBatchId),
                    Revision = task.Revision <= 0 ? 1 : task.Revision
                }, ct).ConfigureAwait(false);
                importedTasks++;
            }

            // Task tags after tasks/tags exist.
            var tagGroups = document.TaskTags
                .Select(tt => new
                {
                    TaskId = mode == ImportMode.ImportAsNew
                        ? MapId(tt.TaskId, () => TaskId.New().ToString())
                        : tt.TaskId,
                    TagId = mode == ImportMode.ImportAsNew
                        ? MapId(tt.TagId, () => TagId.New().ToString())
                        : tt.TagId
                })
                .Where(tt => mode == ImportMode.ImportAsNew
                             || (!existingTasks.Contains(tt.TaskId) && document.Tasks.Any(t => t.Id == tt.TaskId)))
                .GroupBy(tt => tt.TaskId, StringComparer.OrdinalIgnoreCase);

            foreach (var group in tagGroups)
            {
                // For merge-skip, only attach tags for newly imported tasks.
                if (mode != ImportMode.ImportAsNew && existingTasks.Contains(group.Key))
                {
                    continue;
                }

                var tagIds = group
                    .Select(g => TagId.Parse(g.TagId))
                    .Distinct()
                    .ToList();
                await _tags.SetTaskTagsAsync(TaskId.Parse(group.Key), tagIds, ct).ConfigureAwait(false);
            }

            foreach (var template in document.RecurrenceTemplates)
            {
                var id = MapId(template.Id, () => RecurrenceTemplateId.New().ToString());
                if (mode != ImportMode.ImportAsNew && existingTemplates.Contains(id))
                {
                    continue;
                }

                var sourceId = mode == ImportMode.ImportAsNew
                    ? MapId(template.SourceTaskId, () => TaskId.New().ToString())
                    : template.SourceTaskId;

                await _recurrence.InsertAsync(new RecurrenceTemplate
                {
                    Id = RecurrenceTemplateId.Parse(id),
                    SourceTaskId = TaskId.Parse(sourceId),
                    RuleJson = template.RuleJson,
                    ContentSnapshotJson = template.ContentSnapshotJson,
                    TimeZoneId = template.TimeZoneId,
                    NextEvaluationUtc = template.NextEvaluationUtc,
                    IsEnabled = template.IsEnabled,
                    LastSkippedOccurrenceKey = template.LastSkippedOccurrenceKey,
                    CreatedUtc = template.CreatedUtc,
                    UpdatedUtc = template.UpdatedUtc
                }, ct).ConfigureAwait(false);
                importedTemplates++;
            }

            foreach (var dep in document.Dependencies)
            {
                var id = MapId(dep.Id, () => DependencyId.New().ToString());
                if (mode != ImportMode.ImportAsNew && existingDeps.Contains(id))
                {
                    continue;
                }

                var pred = mode == ImportMode.ImportAsNew
                    ? MapId(dep.PredecessorTaskId, () => TaskId.New().ToString())
                    : dep.PredecessorTaskId;
                var succ = mode == ImportMode.ImportAsNew
                    ? MapId(dep.SuccessorTaskId, () => TaskId.New().ToString())
                    : dep.SuccessorTaskId;

                // Skip edges that reference tasks we skipped during merge.
                if (mode != ImportMode.ImportAsNew)
                {
                    var predExists = existingTasks.Contains(pred) || document.Tasks.Any(t => t.Id == pred);
                    var succExists = existingTasks.Contains(succ) || document.Tasks.Any(t => t.Id == succ);
                    if (!predExists || !succExists)
                    {
                        notes.Add("Skipped a dependency with a missing task reference.");
                        continue;
                    }
                }

                await _dependencies.InsertAsync(new DependencyEdge
                {
                    Id = DependencyId.Parse(id),
                    PredecessorTaskId = TaskId.Parse(pred),
                    SuccessorTaskId = TaskId.Parse(succ),
                    Kind = (DependencyKind)dep.Kind,
                    CreatedUtc = dep.CreatedUtc,
                    DeletedUtc = dep.DeletedUtc
                }, ct).ConfigureAwait(false);
                importedDeps++;
            }

            foreach (var session in document.TimeSessions)
            {
                var id = MapId(session.Id, () => SessionId.New().ToString());
                if (mode != ImportMode.ImportAsNew && existingSessions.Contains(id))
                {
                    continue;
                }

                var taskId = mode == ImportMode.ImportAsNew
                    ? MapId(session.TaskId, () => TaskId.New().ToString())
                    : session.TaskId;

                if (mode != ImportMode.ImportAsNew
                    && !existingTasks.Contains(taskId)
                    && document.Tasks.All(t => !t.Id.Equals(taskId, StringComparison.OrdinalIgnoreCase)))
                {
                    notes.Add("Skipped a time session with a missing task reference.");
                    continue;
                }

                var ended = session.EndedUtc;
                int? openSlot = null;
                SessionEndReason? reason = session.EndReason is null
                    ? null
                    : (SessionEndReason)session.EndReason.Value;

                // Preserve one-open-session invariant: never import a second open session.
                if (ended is null)
                {
                    if (openSession is not null || importedSessions > 0 && document.TimeSessions.Count(s => s.EndedUtc is null) > 1)
                    {
                        ended = session.StartedUtc;
                        reason = SessionEndReason.Recovered;
                        notes.Add("Closed an imported open time session to preserve the single-open invariant.");
                    }
                    else if (openSession is null)
                    {
                        // Still close imported open sessions — importing active timers is ambiguous across machines.
                        ended = session.StartedUtc;
                        reason = SessionEndReason.Recovered;
                        notes.Add("Imported open time sessions are closed at their start instant (no phantom elapsed time).");
                    }
                }

                await _sessions.InsertAsync(new TimeSession
                {
                    Id = SessionId.Parse(id),
                    TaskId = TaskId.Parse(taskId),
                    StartedUtc = session.StartedUtc,
                    EndedUtc = ended,
                    EndReason = reason,
                    IsDiscarded = session.IsDiscarded,
                    OpenSlot = openSlot,
                    CreatedUtc = session.CreatedUtc,
                    UpdatedUtc = session.UpdatedUtc
                }, ct).ConfigureAwait(false);
                importedSessions++;
            }

            foreach (var reminder in document.Reminders)
            {
                var id = MapId(reminder.Id, () => ReminderId.New().ToString());
                if (mode != ImportMode.ImportAsNew && existingReminders.Contains(id))
                {
                    continue;
                }

                var taskId = mode == ImportMode.ImportAsNew
                    ? MapId(reminder.TaskId, () => TaskId.New().ToString())
                    : reminder.TaskId;

                var deliveryKey = mode == ImportMode.ImportAsNew
                    ? $"{id}:{reminder.DueUtc:O}"
                    : reminder.DeliveryKey;

                if (mode != ImportMode.ImportAsNew)
                {
                    var existingKey = await _reminders.GetByDeliveryKeyAsync(deliveryKey, ct).ConfigureAwait(false);
                    if (existingKey is not null)
                    {
                        notes.Add("Skipped a reminder with a duplicate delivery key.");
                        continue;
                    }
                }

                await _reminders.InsertAsync(new Reminder
                {
                    Id = ReminderId.Parse(id),
                    TaskId = TaskId.Parse(taskId),
                    DueUtc = reminder.DueUtc,
                    DeliveredUtc = reminder.DeliveredUtc,
                    DismissedUtc = reminder.DismissedUtc,
                    SnoozedUntilUtc = reminder.SnoozedUntilUtc,
                    Kind = (ReminderKind)reminder.Kind,
                    DeliveryKey = deliveryKey,
                    CreatedUtc = reminder.CreatedUtc
                }, ct).ConfigureAwait(false);
                importedReminders++;
            }

            foreach (var audit in document.AuditEvents)
            {
                var id = MapId(audit.Id, () => AuditEventId.New().ToString());
                if (mode != ImportMode.ImportAsNew && existingAudits.Contains(id))
                {
                    continue;
                }

                await _audit.InsertAsync(new AuditEvent
                {
                    Id = AuditEventId.Parse(id),
                    EntityType = audit.EntityType,
                    EntityId = audit.EntityId,
                    Action = (AuditAction)audit.Action,
                    TimestampUtc = audit.TimestampUtc,
                    BeforeJson = audit.BeforeJson,
                    AfterJson = audit.AfterJson,
                    CorrelationId = string.IsNullOrWhiteSpace(audit.CorrelationId)
                        ? null
                        : Guid.Parse(audit.CorrelationId),
                    IsUndone = audit.IsUndone
                }, ct).ConfigureAwait(false);
                importedAudits++;
            }
        }, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Import finished mode={Mode} tasks={Tasks} skipped={Skipped}",
            mode,
            importedTasks,
            skippedTasks);

        return new ImportResult
        {
            Mode = mode,
            ImportedTasks = importedTasks,
            SkippedTasks = skippedTasks,
            ImportedDependencies = importedDeps,
            ImportedSessions = importedSessions,
            ImportedProjects = importedProjects,
            ImportedTags = importedTags,
            ImportedReminders = importedReminders,
            ImportedRecurrenceTemplates = importedTemplates,
            ImportedAuditEvents = importedAudits,
            Notes = notes
        };
    }

    private string ResolveExportPath(string? preferredDirectory, string baseName, string extension)
    {
        var docs = _documents.Resolve();
        string directory;
        if (!string.IsNullOrWhiteSpace(preferredDirectory))
        {
            directory = preferredDirectory;
        }
        else if (docs.IsAvailable && !string.IsNullOrWhiteSpace(docs.Path))
        {
            directory = Path.Combine(docs.Path, "TaskPane", "Exports");
        }
        else
        {
            var dataDir = Path.GetDirectoryName(_backupRunner.LiveDatabasePath)
                          ?? Path.GetTempPath();
            directory = Path.Combine(dataDir, "Exports");
        }

        Directory.CreateDirectory(directory);
        var stamp = _clock.UtcNow.UtcDateTime.ToString("yyyyMMdd-HHmmss");
        var safe = Sanitize($"{baseName}-{stamp}");
        return Path.Combine(directory, safe + extension);
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "export" : cleaned;
    }

    private static ExportDocument ParseDocument(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new BusinessException(ValidationCodes.ImportInvalid, "Import file is empty.");
        }

        if (json.Length > 64 * 1024 * 1024)
        {
            throw new BusinessException(ValidationCodes.ImportInvalid, "Import file is too large.");
        }

        ExportDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<ExportDocument>(json, JsonOptions);
        }
        catch (JsonException)
        {
            throw new BusinessException(ValidationCodes.ImportInvalid, "Import file is not valid JSON.");
        }

        if (document is null)
        {
            throw new BusinessException(ValidationCodes.ImportInvalid, "Import file is empty.");
        }

        if (document.SchemaVersion < 1 || document.SchemaVersion > ExportDocument.CurrentSchemaVersion)
        {
            throw new BusinessException(
                ValidationCodes.ImportSchemaUnsupported,
                $"Unsupported export schemaVersion {document.SchemaVersion}.");
        }

        return document;
    }

    private static ImportPreview BuildPreview(ExportDocument document)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (!string.Equals(document.Application, ExportDocument.ApplicationName, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(document.Application))
        {
            warnings.Add($"Application field is '{document.Application}' (expected TaskPane).");
        }

        var taskIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var task in document.Tasks)
        {
            if (string.IsNullOrWhiteSpace(task.Id) || !Guid.TryParse(task.Id, out _))
            {
                errors.Add("A task has a missing or invalid Id.");
                continue;
            }

            if (!taskIds.Add(task.Id))
            {
                errors.Add($"Duplicate task Id '{task.Id}'.");
            }

            if (string.IsNullOrWhiteSpace(task.Title))
            {
                errors.Add($"Task '{task.Id}' has an empty title.");
            }

            if (!Enum.IsDefined(typeof(TaskPriority), task.Priority)
                || !Enum.IsDefined(typeof(FocusBucket), task.FocusBucket)
                || !Enum.IsDefined(typeof(TaskLifecycleStatus), task.Status))
            {
                errors.Add($"Task '{task.Id}' has an invalid enum value.");
            }
        }

        // Hierarchy: single child level, no self-parent, parent must exist in payload when referenced.
        foreach (var task in document.Tasks)
        {
            if (string.IsNullOrWhiteSpace(task.ParentTaskId))
            {
                continue;
            }

            if (string.Equals(task.ParentTaskId, task.Id, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Task '{task.Id}' cannot be its own parent.");
                continue;
            }

            var parent = document.Tasks.FirstOrDefault(t =>
                t.Id.Equals(task.ParentTaskId, StringComparison.OrdinalIgnoreCase));
            if (parent is null)
            {
                errors.Add($"Task '{task.Id}' references missing parent '{task.ParentTaskId}'.");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(parent.ParentTaskId))
            {
                errors.Add($"Task '{task.Id}' would create nesting deeper than one level.");
            }

            if (document.Tasks.Any(t =>
                    t.ParentTaskId is not null
                    && t.ParentTaskId.Equals(task.Id, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add($"Task '{task.Id}' is both a child and a parent.");
            }
        }

        var projectIds = document.Projects.Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var project in document.Projects)
        {
            if (string.IsNullOrWhiteSpace(project.Id) || !Guid.TryParse(project.Id, out _))
            {
                errors.Add("A project has a missing or invalid Id.");
            }

            if (string.IsNullOrWhiteSpace(project.Name))
            {
                errors.Add($"Project '{project.Id}' has an empty name.");
            }
        }

        foreach (var task in document.Tasks.Where(t => !string.IsNullOrWhiteSpace(t.ProjectId)))
        {
            if (!projectIds.Contains(task.ProjectId!))
            {
                errors.Add($"Task '{task.Id}' references missing project '{task.ProjectId}'.");
            }
        }

        var tagIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in document.Tags)
        {
            if (string.IsNullOrWhiteSpace(tag.Id) || !Guid.TryParse(tag.Id, out _))
            {
                errors.Add("A tag has a missing or invalid Id.");
                continue;
            }

            if (!tagIds.Add(tag.Id))
            {
                errors.Add($"Duplicate tag Id '{tag.Id}'.");
            }

            if (string.IsNullOrWhiteSpace(tag.Name))
            {
                errors.Add($"Tag '{tag.Id}' has an empty name.");
            }
        }

        foreach (var link in document.TaskTags)
        {
            if (!taskIds.Contains(link.TaskId) || !tagIds.Contains(link.TagId))
            {
                errors.Add("A task-tag link references a missing task or tag.");
            }
        }

        var requiredEdges = new List<DependencyEdge>();
        foreach (var dep in document.Dependencies)
        {
            if (string.IsNullOrWhiteSpace(dep.Id) || !Guid.TryParse(dep.Id, out _))
            {
                errors.Add("A dependency has a missing or invalid Id.");
                continue;
            }

            if (!taskIds.Contains(dep.PredecessorTaskId) || !taskIds.Contains(dep.SuccessorTaskId))
            {
                errors.Add($"Dependency '{dep.Id}' references a missing task.");
                continue;
            }

            if (dep.PredecessorTaskId.Equals(dep.SuccessorTaskId, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Dependency '{dep.Id}' is a self-edge.");
                continue;
            }

            if (dep.DeletedUtc is null && dep.Kind == (int)DependencyKind.Required)
            {
                var edge = new DependencyEdge
                {
                    Id = DependencyId.Parse(dep.Id),
                    PredecessorTaskId = TaskId.Parse(dep.PredecessorTaskId),
                    SuccessorTaskId = TaskId.Parse(dep.SuccessorTaskId),
                    Kind = DependencyKind.Required,
                    CreatedUtc = dep.CreatedUtc
                };
                if (DependencyGraph.WouldCreateCycle(requiredEdges, edge.PredecessorTaskId, edge.SuccessorTaskId))
                {
                    errors.Add($"Dependency '{dep.Id}' would create a cycle.");
                }
                else
                {
                    requiredEdges.Add(edge);
                }
            }
        }

        foreach (var session in document.TimeSessions)
        {
            if (string.IsNullOrWhiteSpace(session.Id) || !Guid.TryParse(session.Id, out _))
            {
                errors.Add("A time session has a missing or invalid Id.");
                continue;
            }

            if (!taskIds.Contains(session.TaskId))
            {
                errors.Add($"Time session '{session.Id}' references a missing task.");
            }

            if (session.EndedUtc is { } ended && ended < session.StartedUtc)
            {
                errors.Add($"Time session '{session.Id}' has a negative duration.");
            }
        }

        var openCount = document.TimeSessions.Count(s => s.EndedUtc is null && !s.IsDiscarded);
        if (openCount > 1)
        {
            warnings.Add($"Export contains {openCount} open time sessions; import will close them safely.");
        }

        var templateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var template in document.RecurrenceTemplates)
        {
            if (string.IsNullOrWhiteSpace(template.Id) || !Guid.TryParse(template.Id, out _))
            {
                errors.Add("A recurrence template has a missing or invalid Id.");
                continue;
            }

            if (!templateIds.Add(template.Id))
            {
                errors.Add($"Duplicate recurrence template Id '{template.Id}'.");
            }

            if (!taskIds.Contains(template.SourceTaskId))
            {
                errors.Add($"Recurrence template '{template.Id}' references a missing source task.");
            }

            if (string.IsNullOrWhiteSpace(template.RuleJson) || string.IsNullOrWhiteSpace(template.TimeZoneId))
            {
                errors.Add($"Recurrence template '{template.Id}' is incomplete.");
            }
        }

        var occurrenceKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var task in document.Tasks.Where(t =>
                     !string.IsNullOrWhiteSpace(t.RecurrenceTemplateId)
                     && !string.IsNullOrWhiteSpace(t.OccurrenceKey)))
        {
            var key = $"{task.RecurrenceTemplateId}|{task.OccurrenceKey}";
            if (!occurrenceKeys.Add(key))
            {
                errors.Add(
                    $"Duplicate occurrence key '{task.OccurrenceKey}' for template '{task.RecurrenceTemplateId}'.");
            }

            if (!templateIds.Contains(task.RecurrenceTemplateId!))
            {
                // Template may be absent if detached historically — warn, don't hard-fail.
                warnings.Add($"Task '{task.Id}' references recurrence template '{task.RecurrenceTemplateId}' not in export.");
            }
        }

        var deliveryKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reminder in document.Reminders)
        {
            if (string.IsNullOrWhiteSpace(reminder.Id) || !Guid.TryParse(reminder.Id, out _))
            {
                errors.Add("A reminder has a missing or invalid Id.");
                continue;
            }

            if (!taskIds.Contains(reminder.TaskId))
            {
                errors.Add($"Reminder '{reminder.Id}' references a missing task.");
            }

            if (string.IsNullOrWhiteSpace(reminder.DeliveryKey) || !deliveryKeys.Add(reminder.DeliveryKey))
            {
                errors.Add($"Reminder '{reminder.Id}' has a missing or duplicate delivery key.");
            }
        }

        // Cross-check hierarchy the same way runtime would.
        try
        {
            var mapped = document.Tasks.Select(t => new TaskItem
            {
                Id = TaskId.Parse(t.Id),
                ParentTaskId = string.IsNullOrWhiteSpace(t.ParentTaskId) ? null : TaskId.Parse(t.ParentTaskId),
                Title = t.Title,
                CreatedUtc = t.CreatedUtc,
                UpdatedUtc = t.UpdatedUtc
            }).ToList();

            foreach (var child in mapped.Where(t => t.ParentTaskId is not null))
            {
                var parent = mapped.FirstOrDefault(t => t.Id.Equals(child.ParentTaskId));
                var childrenOfChild = mapped
                    .Where(t => t.ParentTaskId is { } pid && pid.Equals(child.Id))
                    .ToList();
                TaskHierarchyValidator.EnsureCanAssignParent(child, parent, childrenOfChild);
            }
        }
        catch (BusinessException ex)
        {
            errors.Add(ex.Message);
        }
        catch (FormatException)
        {
            errors.Add("One or more task IDs could not be parsed.");
        }

        return new ImportPreview
        {
            Document = document,
            TaskCount = document.Tasks.Count,
            SubtaskCount = document.Tasks.Count(t => t.ParentTaskId is not null),
            DeletedTaskCount = document.Tasks.Count(t => t.DeletedUtc is not null),
            DependencyCount = document.Dependencies.Count,
            ProjectCount = document.Projects.Count,
            TagCount = document.Tags.Count,
            SessionCount = document.TimeSessions.Count,
            RecurrenceTemplateCount = document.RecurrenceTemplates.Count,
            ReminderCount = document.Reminders.Count,
            AuditEventCount = document.AuditEvents.Count,
            Warnings = warnings,
            Errors = errors
        };
    }

    private static void AppendMarkdownTask(StringBuilder sb, ExportTaskDto task, int depth)
    {
        var indent = depth == 0 ? "" : "  ";
        var status = task.Status == (int)TaskLifecycleStatus.Completed ? "x" : " ";
        var title = EscapeMarkdown(task.Title);
        sb.AppendLine($"{indent}- [{status}] {title}");
        if (!string.IsNullOrWhiteSpace(task.Notes))
        {
            var preview = EscapeMarkdown(task.Notes.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n', 2)[0]);
            if (preview.Length > 120)
            {
                preview = preview[..117] + "...";
            }

            sb.AppendLine($"{indent}  _{preview}_");
        }

        if (!string.IsNullOrWhiteSpace(task.DeadlineLocalDate))
        {
            sb.AppendLine($"{indent}  - Due: {task.DeadlineLocalDate} {task.DeadlineLocalTime}".TrimEnd());
        }
    }

    private static string EscapeMarkdown(string value)
        => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    private static ExportTaskDto MapTask(TaskItem t) => new()
    {
        Id = t.Id.ToString(),
        ParentTaskId = t.ParentTaskId?.ToString(),
        Title = t.Title,
        Notes = t.Notes,
        Priority = (int)t.Priority,
        FocusBucket = (int)t.FocusBucket,
        Status = (int)t.Status,
        ManualSortKey = t.ManualSortKey,
        DeadlineLocalDate = t.DeadlineLocalDate,
        DeadlineLocalTime = t.DeadlineLocalTime,
        DeadlineTimeZoneId = t.DeadlineTimeZoneId,
        SleepUntilUtc = t.SleepUntilUtc,
        SleepIndefinitely = t.SleepIndefinitely,
        WaitingFor = t.WaitingFor,
        WaitingFollowUpUtc = t.WaitingFollowUpUtc,
        ProjectId = t.ProjectId?.ToString(),
        RecurrenceTemplateId = t.RecurrenceTemplateId?.ToString(),
        OccurrenceKey = t.OccurrenceKey,
        CreatedUtc = t.CreatedUtc,
        UpdatedUtc = t.UpdatedUtc,
        CompletedUtc = t.CompletedUtc,
        DeletedUtc = t.DeletedUtc,
        DeletionBatchId = t.DeletionBatchId?.ToString("D"),
        Revision = t.Revision
    };

    private static ExportProjectDto MapProject(Project p) => new()
    {
        Id = p.Id.ToString(),
        Name = p.Name,
        Color = p.Color,
        ArchivedUtc = p.ArchivedUtc,
        CreatedUtc = p.CreatedUtc,
        UpdatedUtc = p.UpdatedUtc
    };

    private static ExportTagDto MapTag(Tag t) => new()
    {
        Id = t.Id.ToString(),
        Name = t.Name,
        Color = t.Color,
        CreatedUtc = t.CreatedUtc
    };

    private static ExportDependencyDto MapDependency(DependencyEdge d) => new()
    {
        Id = d.Id.ToString(),
        PredecessorTaskId = d.PredecessorTaskId.ToString(),
        SuccessorTaskId = d.SuccessorTaskId.ToString(),
        Kind = (int)d.Kind,
        CreatedUtc = d.CreatedUtc,
        DeletedUtc = d.DeletedUtc
    };

    private static ExportTimeSessionDto MapSession(TimeSession s) => new()
    {
        Id = s.Id.ToString(),
        TaskId = s.TaskId.ToString(),
        StartedUtc = s.StartedUtc,
        EndedUtc = s.EndedUtc,
        EndReason = s.EndReason is null ? null : (int)s.EndReason.Value,
        IsDiscarded = s.IsDiscarded,
        CreatedUtc = s.CreatedUtc,
        UpdatedUtc = s.UpdatedUtc
    };

    private static ExportRecurrenceTemplateDto MapTemplate(RecurrenceTemplate t) => new()
    {
        Id = t.Id.ToString(),
        SourceTaskId = t.SourceTaskId.ToString(),
        RuleJson = t.RuleJson,
        ContentSnapshotJson = t.ContentSnapshotJson,
        TimeZoneId = t.TimeZoneId,
        NextEvaluationUtc = t.NextEvaluationUtc,
        IsEnabled = t.IsEnabled,
        LastSkippedOccurrenceKey = t.LastSkippedOccurrenceKey,
        CreatedUtc = t.CreatedUtc,
        UpdatedUtc = t.UpdatedUtc
    };

    private static ExportReminderDto MapReminder(Reminder r) => new()
    {
        Id = r.Id.ToString(),
        TaskId = r.TaskId.ToString(),
        DueUtc = r.DueUtc,
        DeliveredUtc = r.DeliveredUtc,
        DismissedUtc = r.DismissedUtc,
        SnoozedUntilUtc = r.SnoozedUntilUtc,
        Kind = (int)r.Kind,
        DeliveryKey = r.DeliveryKey,
        CreatedUtc = r.CreatedUtc
    };

    private static ExportAuditEventDto MapAudit(AuditEvent a) => new()
    {
        Id = a.Id.ToString(),
        EntityType = a.EntityType,
        EntityId = a.EntityId,
        Action = (int)a.Action,
        TimestampUtc = a.TimestampUtc,
        BeforeJson = a.BeforeJson,
        AfterJson = a.AfterJson,
        CorrelationId = a.CorrelationId?.ToString("D"),
        IsUndone = a.IsUndone
    };
}
