using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using TaskPane.Core.Ids;

namespace TaskPane.Core.Dependencies;

/// <summary>Pure helpers for required-edge DAG validation and blocked derivation.</summary>
public static class DependencyGraph
{
    public static bool WouldCreateCycle(
        IEnumerable<DependencyEdge> existingRequiredEdges,
        TaskId predecessorId,
        TaskId successorId)
    {
        if (predecessorId.Equals(successorId))
        {
            return true;
        }

        // Adding pred -> succ creates a cycle if succ can already reach pred.
        var adjacency = BuildOutgoing(existingRequiredEdges);
        return CanReach(adjacency, successorId, predecessorId);
    }

    public static bool CanReach(
        IReadOnlyDictionary<TaskId, List<TaskId>> adjacency,
        TaskId from,
        TaskId target)
    {
        var stack = new Stack<TaskId>();
        var seen = new HashSet<TaskId>();
        stack.Push(from);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!seen.Add(current))
            {
                continue;
            }

            if (current.Equals(target))
            {
                return true;
            }

            if (!adjacency.TryGetValue(current, out var next))
            {
                continue;
            }

            foreach (var neighbor in next)
            {
                stack.Push(neighbor);
            }
        }

        return false;
    }

    public static Dictionary<TaskId, List<TaskId>> BuildOutgoing(IEnumerable<DependencyEdge> requiredEdges)
    {
        var map = new Dictionary<TaskId, List<TaskId>>();
        foreach (var edge in requiredEdges.Where(e => !e.IsDeleted && e.Kind == DependencyKind.Required))
        {
            if (!map.TryGetValue(edge.PredecessorTaskId, out var list))
            {
                list = [];
                map[edge.PredecessorTaskId] = list;
            }

            list.Add(edge.SuccessorTaskId);
        }

        return map;
    }

    /// <summary>
    /// A successor is blocked when any required predecessor is unfinished, missing, or soft-deleted.
    /// </summary>
    public static BlockedState EvaluateBlocked(
        TaskId successorId,
        IEnumerable<DependencyEdge> edges,
        IReadOnlyDictionary<TaskId, TaskItem> tasksById)
    {
        var required = edges
            .Where(e => !e.IsDeleted
                        && e.Kind == DependencyKind.Required
                        && e.SuccessorTaskId.Equals(successorId))
            .ToList();

        if (required.Count == 0)
        {
            return BlockedState.NotBlocked;
        }

        var unfinished = new List<TaskId>();
        var deletedOrMissing = new List<TaskId>();

        foreach (var edge in required)
        {
            if (!tasksById.TryGetValue(edge.PredecessorTaskId, out var pred))
            {
                deletedOrMissing.Add(edge.PredecessorTaskId);
                continue;
            }

            if (pred.IsDeleted)
            {
                deletedOrMissing.Add(pred.Id);
                continue;
            }

            if (!pred.IsCompleted)
            {
                unfinished.Add(pred.Id);
            }
        }

        if (unfinished.Count == 0 && deletedOrMissing.Count == 0)
        {
            return BlockedState.NotBlocked;
        }

        return new BlockedState(true, unfinished, deletedOrMissing);
    }
}

public sealed class BlockedState
{
    public static BlockedState NotBlocked { get; } = new(false, [], []);

    public BlockedState(
        bool isBlocked,
        IReadOnlyList<TaskId> unfinishedPredecessorIds,
        IReadOnlyList<TaskId> deletedOrMissingPredecessorIds)
    {
        IsBlocked = isBlocked;
        UnfinishedPredecessorIds = unfinishedPredecessorIds;
        DeletedOrMissingPredecessorIds = deletedOrMissingPredecessorIds;
    }

    public bool IsBlocked { get; }
    public IReadOnlyList<TaskId> UnfinishedPredecessorIds { get; }
    public IReadOnlyList<TaskId> DeletedOrMissingPredecessorIds { get; }
    public bool HasDeletedPredecessorWarning => DeletedOrMissingPredecessorIds.Count > 0;
}
