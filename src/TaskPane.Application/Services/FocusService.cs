using TaskPane.Application.Models;
using TaskPane.Core.Deadline;
using TaskPane.Core.Dependencies;
using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using TaskPane.Core.Ids;
using TaskPane.Core.Interfaces;
using TaskPane.Core.Sleep;

namespace TaskPane.Application.Services;

/// <summary>
/// Focus filtering, search, sleep visibility and optional sort modes.
/// Deadlines never force priority or manual order changes.
/// </summary>
public sealed class FocusService
{
    private readonly ITaskRepository _tasks;
    private readonly ITagRepository _tags;
    private readonly IDependencyRepository _dependencies;

    public FocusService(ITaskRepository tasks, ITagRepository tags, IDependencyRepository dependencies)
    {
        _tasks = tasks;
        _tags = tags;
        _dependencies = dependencies;
    }

    public async Task<IReadOnlyList<TaskItem>> QueryRootsAsync(
        TaskQuery query,
        DateTimeOffset? utcNow = null,
        CancellationToken cancellationToken = default)
    {
        var now = utcNow ?? DateTimeOffset.UtcNow;
        var all = await _tasks.ListAsync(includeDeleted: false, cancellationToken).ConfigureAwait(false);
        var allIncludingDeleted = query.OnlyBlocked || query.HideBlocked
            ? await _tasks.ListAsync(includeDeleted: true, cancellationToken).ConfigureAwait(false)
            : all;
        var blockedIds = await ResolveBlockedIdsAsync(query, allIncludingDeleted, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlySet<TaskId>? tagged = null;
        if (query.TagId is { } tagFilter)
        {
            var map = await _tags.GetTagIdsForTasksAsync(all.Select(t => t.Id).ToList(), cancellationToken)
                .ConfigureAwait(false);
            tagged = map.Where(kv => kv.Value.Contains(tagFilter)).Select(kv => kv.Key).ToHashSet();
        }

        var roots = all.Where(t => t.IsRoot)
            .Where(t => Matches(t, query, now, tagged, parent: null, blockedIds));
        return Sort(roots, query.SortMode).ToList();
    }

    public async Task<IReadOnlyList<TaskItem>> QueryChildrenAsync(
        TaskId parentId,
        TaskQuery query,
        DateTimeOffset? utcNow = null,
        CancellationToken cancellationToken = default)
    {
        var now = utcNow ?? DateTimeOffset.UtcNow;
        var parent = await _tasks.GetByIdAsync(parentId, cancellationToken).ConfigureAwait(false);
        var children = await _tasks.ListChildrenAsync(parentId, includeDeleted: false, cancellationToken)
            .ConfigureAwait(false);
        var allIncludingDeleted = query.OnlyBlocked || query.HideBlocked
            ? await _tasks.ListAsync(includeDeleted: true, cancellationToken).ConfigureAwait(false)
            : children;
        var blockedIds = await ResolveBlockedIdsAsync(query, allIncludingDeleted, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlySet<TaskId>? tagged = null;
        if (query.TagId is { } tagFilter)
        {
            var map = await _tags.GetTagIdsForTasksAsync(children.Select(t => t.Id).ToList(), cancellationToken)
                .ConfigureAwait(false);
            tagged = map.Where(kv => kv.Value.Contains(tagFilter)).Select(kv => kv.Key).ToHashSet();
        }

        var filtered = children.Where(t => Matches(t, query, now, tagged, parent, blockedIds));
        return Sort(filtered, query.SortMode).ToList();
    }

    public async Task<IReadOnlyList<TaskItem>> ListSleepingRootsAsync(
        DateTimeOffset? utcNow = null,
        CancellationToken cancellationToken = default)
    {
        var now = utcNow ?? DateTimeOffset.UtcNow;
        var all = await _tasks.ListAsync(includeDeleted: false, cancellationToken).ConfigureAwait(false);
        return all
            .Where(t => t.IsRoot && !t.IsCompleted && SleepEvaluator.IsCurrentlySleeping(t, now))
            .OrderBy(t => t.ManualSortKey)
            .ToList();
    }

    public static bool Matches(
        TaskItem task,
        TaskQuery query,
        DateTimeOffset utcNow,
        IReadOnlySet<TaskId>? taggedTaskIds,
        TaskItem? parent,
        IReadOnlySet<TaskId>? blockedTaskIds = null)
    {
        if (query.OnlyCompleted)
        {
            if (!task.IsCompleted)
            {
                return false;
            }
        }
        else if (!query.IncludeCompleted && task.IsCompleted)
        {
            return false;
        }

        if (query.FocusBucket is { } focus && task.FocusBucket != focus)
        {
            return false;
        }

        if (query.ProjectId is { } projectId && task.ProjectId?.Equals(projectId) != true)
        {
            return false;
        }

        if (taggedTaskIds is not null && !taggedTaskIds.Contains(task.Id))
        {
            return false;
        }

        var sleeping = SleepEvaluator.IsCurrentlySleeping(task, utcNow);
        var hiddenByParent = parent is not null && SleepEvaluator.IsCurrentlySleeping(parent, utcNow);

        if (query.OnlySleeping)
        {
            if (!sleeping)
            {
                return false;
            }
        }
        else if (query.OverlayVisibility)
        {
            if (sleeping || hiddenByParent)
            {
                return false;
            }
        }
        else if (!query.IncludeSleeping && (sleeping || hiddenByParent))
        {
            return false;
        }

        if (query.OnlyWaiting && !task.IsWaiting)
        {
            return false;
        }

        var isBlocked = blockedTaskIds?.Contains(task.Id) == true;
        if (query.OnlyBlocked && !isBlocked)
        {
            return false;
        }

        if (query.HideBlocked && isBlocked)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            var term = query.SearchText.Trim();
            var inTitle = task.Title.Contains(term, StringComparison.OrdinalIgnoreCase);
            var inNotes = task.Notes?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;
            if (!inTitle && !inNotes)
            {
                return false;
            }
        }

        return true;
    }

    private async Task<IReadOnlySet<TaskId>?> ResolveBlockedIdsAsync(
        TaskQuery query,
        IReadOnlyList<TaskItem> tasksForLookup,
        CancellationToken cancellationToken)
    {
        if (!query.OnlyBlocked && !query.HideBlocked)
        {
            return null;
        }

        var edges = await _dependencies.ListAsync(includeDeleted: false, cancellationToken).ConfigureAwait(false);
        var byId = tasksForLookup.ToDictionary(t => t.Id);
        var blocked = new HashSet<TaskId>();
        foreach (var task in tasksForLookup.Where(t => !t.IsDeleted))
        {
            if (DependencyGraph.EvaluateBlocked(task.Id, edges, byId).IsBlocked)
            {
                blocked.Add(task.Id);
            }
        }

        return blocked;
    }

    public static IEnumerable<TaskItem> Sort(IEnumerable<TaskItem> tasks, TaskSortMode mode)
    {
        return mode switch
        {
            TaskSortMode.Priority => tasks
                .OrderBy(t => t.Priority)
                .ThenBy(t => t.ManualSortKey)
                .ThenBy(t => t.CreatedUtc),
            TaskSortMode.Deadline => tasks
                .OrderBy(t => t, Comparer<TaskItem>.Create(DeadlineEvaluator.CompareDeadlines))
                .ThenBy(t => t.ManualSortKey)
                .ThenBy(t => t.CreatedUtc),
            TaskSortMode.CreatedDesc => tasks
                .OrderByDescending(t => t.CreatedUtc)
                .ThenBy(t => t.ManualSortKey),
            _ => tasks
                .OrderBy(t => t.ManualSortKey)
                .ThenBy(t => t.CreatedUtc)
        };
    }
}
