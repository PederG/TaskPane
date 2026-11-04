using System.Text.Json;
using Microsoft.Extensions.Logging;
using TaskPane.Application.Models;
using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using TaskPane.Core.Errors;
using TaskPane.Core.Ids;
using TaskPane.Core.Interfaces;
using TaskPane.Core.Recurrence;

namespace TaskPane.Application.Services;

public sealed class RecurrenceService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly IRecurrenceTemplateRepository _templates;
    private readonly ITaskRepository _tasks;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly ILogger<RecurrenceService> _logger;

    public RecurrenceService(
        IRecurrenceTemplateRepository templates,
        ITaskRepository tasks,
        IUnitOfWork unitOfWork,
        IClock clock,
        ILogger<RecurrenceService> logger)
    {
        _templates = templates;
        _tasks = tasks;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _logger = logger;
    }

    public async Task<RecurrenceTemplate> AttachAsync(
        AttachRecurrenceRequest request,
        CancellationToken cancellationToken = default)
    {
        RecurrenceCalculator.Validate(request.Rule);
        var task = await RequireTaskAsync(request.TaskId, cancellationToken).ConfigureAwait(false);
        if (task.ParentTaskId is { } parentId)
        {
            var parent = await _tasks.GetByIdAsync(parentId, cancellationToken).ConfigureAwait(false);
            if (parent?.ParentTaskId is not null)
            {
                throw new BusinessException(
                    ValidationCodes.HierarchyDepth,
                    "Recurring generation cannot violate one-level nesting.");
            }
        }

        var children = await _tasks.ListChildrenAsync(task.Id, includeDeleted: false, cancellationToken)
            .ConfigureAwait(false);
        var snapshot = BuildSnapshot(task, children, request);
        var tzId = string.IsNullOrWhiteSpace(request.TimeZoneId)
            ? TimeZoneInfo.Local.Id
            : request.TimeZoneId.Trim();
        var now = _clock.UtcNow;
        var template = new RecurrenceTemplate
        {
            Id = RecurrenceTemplateId.New(),
            SourceTaskId = task.Id,
            RuleJson = JsonSerializer.Serialize(request.Rule, JsonOptions),
            ContentSnapshotJson = JsonSerializer.Serialize(snapshot, JsonOptions),
            TimeZoneId = tzId,
            IsEnabled = true,
            NextEvaluationUtc = now,
            CreatedUtc = now,
            UpdatedUtc = now
        };

        var anchor = RecurrenceCalculator.GetAnchor(request.Rule);
        var localTime = RecurrenceCalculator.GetLocalTime(request.Rule);
        var anchorKey = OccurrenceKey.FromLocal(anchor, localTime);

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await _templates.InsertAsync(template, ct).ConfigureAwait(false);

            if (request.ConvertSourceToFirstOccurrence)
            {
                task.RecurrenceTemplateId = template.Id;
                task.OccurrenceKey = anchorKey;
                if (snapshot.SetDeadlineToOccurrenceDate)
                {
                    task.DeadlineLocalDate = anchor.ToString("yyyy-MM-dd");
                    task.DeadlineLocalTime = localTime?.ToString("HH:mm");
                    task.DeadlineTimeZoneId = tzId;
                }

                task.UpdatedUtc = now;
                task.Revision++;
                await _tasks.UpdateAsync(task, ct).ConfigureAwait(false);
            }
            else
            {
                await MaterializeOccurrenceAsync(template, request.Rule, snapshot, anchor, localTime, ct)
                    .ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Attached recurrence template {TemplateId}", template.Id);
        return template;
    }

    public async Task<RecurrenceTemplate?> GetTemplateAsync(
        RecurrenceTemplateId id,
        CancellationToken cancellationToken = default)
        => await _templates.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

    public RecurrenceRule? ParseRule(RecurrenceTemplate template)
        => JsonSerializer.Deserialize<RecurrenceRule>(template.RuleJson, JsonOptions);

    public RecurrenceContentSnapshot? ParseSnapshot(RecurrenceTemplate template)
        => JsonSerializer.Deserialize<RecurrenceContentSnapshot>(template.ContentSnapshotJson, JsonOptions);

    /// <summary>
    /// Missed-occurrence policy: never accumulate an unlimited backlog.
    /// Materialize at most the latest due occurrence (skip earlier missed keys)
    /// and ensure the next future occurrence exists when the current open one is completed.
    /// </summary>
    public async Task EvaluateAllAsync(CancellationToken cancellationToken = default)
    {
        var templates = await _templates.ListEnabledAsync(cancellationToken).ConfigureAwait(false);
        foreach (var template in templates)
        {
            await EvaluateTemplateAsync(template.Id, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<TaskItem?> EvaluateTemplateAsync(
        RecurrenceTemplateId templateId,
        CancellationToken cancellationToken = default)
    {
        var template = await _templates.GetByIdAsync(templateId, cancellationToken).ConfigureAwait(false);
        if (template is null || !template.IsEnabled)
        {
            return null;
        }

        var rule = ParseRule(template)
                   ?? throw new BusinessException(ValidationCodes.InvalidRecurrenceRule, "Corrupt recurrence rule.");
        var snapshot = ParseSnapshot(template)
                       ?? throw new BusinessException(ValidationCodes.InvalidRecurrenceRule, "Corrupt content snapshot.");
        var tz = LocalScheduleTime.Resolve(template.TimeZoneId);
        var nowLocal = TimeZoneInfo.ConvertTime(_clock.UtcNow, tz);
        var today = DateOnly.FromDateTime(nowLocal.DateTime);
        var localTime = RecurrenceCalculator.GetLocalTime(rule);

        var dueDate = RecurrenceCalculator.LatestOnOrBefore(rule, today);
        if (dueDate is null)
        {
            return null;
        }

        // If timed and today's occurrence is not yet due, use previous occurrence date.
        if (localTime is { } timed
            && dueDate.Value == today
            && TimeOnly.FromDateTime(nowLocal.DateTime) < timed)
        {
            dueDate = RecurrenceCalculator.LatestOnOrBefore(rule, today.AddDays(-1));
            if (dueDate is null)
            {
                return null;
            }
        }

        var dueKey = OccurrenceKey.FromLocal(dueDate.Value, localTime);
        var existing = await _tasks.FindByOccurrenceKeyAsync(template.Id, dueKey, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            // Ensure next after a completed current occurrence.
            if (existing.IsCompleted)
            {
                return await EnsureNextAfterAsync(template, rule, snapshot, dueDate.Value, cancellationToken)
                    .ConfigureAwait(false);
            }

            return existing;
        }

        // Skip earlier missed keys: record the previous candidate if any open gap exists.
        var occurrences = await _tasks.ListByRecurrenceTemplateAsync(template.Id, includeDeleted: true, cancellationToken)
            .ConfigureAwait(false);
        var open = occurrences.Where(o => !o.IsDeleted && !o.IsCompleted).ToList();
        if (open.Count > 0)
        {
            // Cap: do not pile catch-up instances while an open occurrence already exists.
            template.LastSkippedOccurrenceKey = dueKey;
            template.NextEvaluationUtc = _clock.UtcNow.AddMinutes(15);
            template.UpdatedUtc = _clock.UtcNow;
            await _templates.UpdateAsync(template, cancellationToken).ConfigureAwait(false);
            return open.OrderBy(o => o.OccurrenceKey).Last();
        }

        TaskItem? created = null;
        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            created = await MaterializeOccurrenceAsync(template, rule, snapshot, dueDate.Value, localTime, ct)
                .ConfigureAwait(false);
            template.NextEvaluationUtc = _clock.UtcNow.AddHours(1);
            template.UpdatedUtc = _clock.UtcNow;
            await _templates.UpdateAsync(template, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        return created;
    }

    public async Task<TaskItem?> EnsureNextAfterCompletionAsync(
        TaskItem completed,
        CancellationToken cancellationToken = default)
    {
        if (completed.RecurrenceTemplateId is not { } templateId
            || string.IsNullOrWhiteSpace(completed.OccurrenceKey))
        {
            return null;
        }

        if (!OccurrenceKey.TryParse(completed.OccurrenceKey, out var date, out _))
        {
            return null;
        }

        var template = await _templates.GetByIdAsync(templateId, cancellationToken).ConfigureAwait(false);
        if (template is null || !template.IsEnabled)
        {
            return null;
        }

        var rule = ParseRule(template)
                   ?? throw new BusinessException(ValidationCodes.InvalidRecurrenceRule, "Corrupt recurrence rule.");
        var snapshot = ParseSnapshot(template)
                       ?? throw new BusinessException(ValidationCodes.InvalidRecurrenceRule, "Corrupt content snapshot.");
        return await EnsureNextAfterAsync(template, rule, snapshot, date, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TaskItem> EditOccurrenceAsync(
        EditOccurrenceRequest request,
        CancellationToken cancellationToken = default)
    {
        var task = await RequireTaskAsync(request.TaskId, cancellationToken).ConfigureAwait(false);
        if (task.RecurrenceTemplateId is not { } templateId)
        {
            throw new BusinessException(ValidationCodes.RecurrenceNotAttached, "Task is not a recurring occurrence.");
        }

        var template = await _templates.GetByIdAsync(templateId, cancellationToken).ConfigureAwait(false)
                       ?? throw new BusinessException(ValidationCodes.RecurrenceTemplateNotFound, "Template missing.");

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            if (!string.IsNullOrWhiteSpace(request.Title))
            {
                task.Title = request.Title.Trim();
            }

            if (request.Notes is not null)
            {
                task.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes;
            }

            if (request.Scope == OccurrenceEditScope.ThisOccurrenceOnly)
            {
                // Detach so future generation is unaffected; keep OccurrenceKey for history/dedup.
                task.RecurrenceTemplateId = null;
            }
            else
            {
                var snapshot = ParseSnapshot(template) ?? new RecurrenceContentSnapshot { Title = task.Title };
                snapshot.Title = task.Title;
                snapshot.Notes = task.Notes;
                if (request.CopyNotes is { } copyNotes)
                {
                    snapshot.CopyNotes = copyNotes;
                }

                if (request.CopySubtasks is { } copySubtasks)
                {
                    snapshot.CopySubtasks = copySubtasks;
                }

                if (request.FutureRule is { } futureRule)
                {
                    RecurrenceCalculator.Validate(futureRule);
                    template.RuleJson = JsonSerializer.Serialize(futureRule, JsonOptions);
                }

                template.ContentSnapshotJson = JsonSerializer.Serialize(snapshot, JsonOptions);
                template.UpdatedUtc = _clock.UtcNow;
                await _templates.UpdateAsync(template, ct).ConfigureAwait(false);
            }

            task.UpdatedUtc = _clock.UtcNow;
            task.Revision++;
            await _tasks.UpdateAsync(task, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        return task;
    }

    public async Task DisableAsync(RecurrenceTemplateId templateId, CancellationToken cancellationToken = default)
    {
        var template = await _templates.GetByIdAsync(templateId, cancellationToken).ConfigureAwait(false)
                       ?? throw new BusinessException(ValidationCodes.RecurrenceTemplateNotFound, "Template missing.");
        template.IsEnabled = false;
        template.UpdatedUtc = _clock.UtcNow;
        await _templates.UpdateAsync(template, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TaskItem?> EnsureNextAfterAsync(
        RecurrenceTemplate template,
        RecurrenceRule rule,
        RecurrenceContentSnapshot snapshot,
        DateOnly afterDate,
        CancellationToken cancellationToken)
    {
        var nextDate = RecurrenceCalculator.NextAfter(rule, afterDate);
        if (nextDate is null)
        {
            return null;
        }

        var localTime = RecurrenceCalculator.GetLocalTime(rule);
        var key = OccurrenceKey.FromLocal(nextDate.Value, localTime);
        var existing = await _tasks.FindByOccurrenceKeyAsync(template.Id, key, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        TaskItem? created = null;
        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            created = await MaterializeOccurrenceAsync(template, rule, snapshot, nextDate.Value, localTime, ct)
                .ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        return created;
    }

    private async Task<TaskItem> MaterializeOccurrenceAsync(
        RecurrenceTemplate template,
        RecurrenceRule rule,
        RecurrenceContentSnapshot snapshot,
        DateOnly localDate,
        TimeOnly? localTime,
        CancellationToken cancellationToken)
    {
        var key = OccurrenceKey.FromLocal(localDate, localTime);
        var existing = await _tasks.FindByOccurrenceKeyAsync(template.Id, key, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var source = await _tasks.GetByIdAsync(template.SourceTaskId, cancellationToken).ConfigureAwait(false);
        TaskId? parentId = null;
        if (source?.ParentTaskId is { } sourceParent)
        {
            // Recurring subtask stays under the same parent — never deepen hierarchy.
            var parent = await _tasks.GetByIdAsync(sourceParent, cancellationToken).ConfigureAwait(false);
            if (parent is not null && !parent.IsDeleted && parent.ParentTaskId is null)
            {
                parentId = parent.Id;
            }
        }

        var now = _clock.UtcNow;
        var sortKey = await _tasks
            .GetNextSortKeyAsync(parentId, parentId is null ? snapshot.FocusBucket : null, cancellationToken)
            .ConfigureAwait(false);

        var task = new TaskItem
        {
            Id = TaskId.New(),
            ParentTaskId = parentId,
            Title = snapshot.Title,
            Notes = snapshot.CopyNotes ? snapshot.Notes : null,
            Priority = snapshot.Priority,
            FocusBucket = snapshot.FocusBucket,
            Status = TaskLifecycleStatus.Open,
            ManualSortKey = sortKey,
            DeadlineLocalDate = snapshot.SetDeadlineToOccurrenceDate ? localDate.ToString("yyyy-MM-dd") : null,
            DeadlineLocalTime = snapshot.SetDeadlineToOccurrenceDate ? localTime?.ToString("HH:mm") : null,
            DeadlineTimeZoneId = snapshot.SetDeadlineToOccurrenceDate ? template.TimeZoneId : null,
            ProjectId = ProjectId.TryParse(snapshot.ProjectId, out var projectId) ? projectId : null,
            RecurrenceTemplateId = template.Id,
            OccurrenceKey = key,
            CreatedUtc = now,
            UpdatedUtc = now,
            Revision = 1
        };

        await _tasks.InsertAsync(task, cancellationToken).ConfigureAwait(false);

        // Dependencies are NOT remapped onto occurrences (Phase 5 policy).
        if (snapshot.CopySubtasks && parentId is null)
        {
            double childSort = 1000;
            foreach (var sub in snapshot.Subtasks)
            {
                var child = new TaskItem
                {
                    Id = TaskId.New(),
                    ParentTaskId = task.Id,
                    Title = sub.Title,
                    Notes = snapshot.CopyNotes ? sub.Notes : null,
                    Priority = TaskPriority.Normal,
                    FocusBucket = snapshot.FocusBucket,
                    Status = TaskLifecycleStatus.Open,
                    ManualSortKey = childSort,
                    CreatedUtc = now,
                    UpdatedUtc = now,
                    Revision = 1
                };
                childSort += 1000;
                await _tasks.InsertAsync(child, cancellationToken).ConfigureAwait(false);
            }
        }

        return task;
    }

    private static RecurrenceContentSnapshot BuildSnapshot(
        TaskItem task,
        IReadOnlyList<TaskItem> children,
        AttachRecurrenceRequest request)
        => new()
        {
            Title = task.Title,
            Notes = task.Notes,
            Priority = task.Priority,
            FocusBucket = task.FocusBucket,
            ProjectId = task.ProjectId?.ToString(),
            CopyNotes = request.CopyNotes,
            CopySubtasks = request.CopySubtasks,
            SetDeadlineToOccurrenceDate = request.SetDeadlineToOccurrenceDate,
            Subtasks = children.Select(c => new RecurrenceSubtaskSnapshot
            {
                Title = c.Title,
                Notes = c.Notes
            }).ToList()
        };

    private async Task<TaskItem> RequireTaskAsync(TaskId id, CancellationToken cancellationToken)
    {
        var task = await _tasks.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (task is null || task.IsDeleted)
        {
            throw new BusinessException(ValidationCodes.TaskNotFound, $"Task {id} was not found.");
        }

        return task;
    }
}
