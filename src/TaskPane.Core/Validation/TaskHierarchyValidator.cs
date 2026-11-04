using TaskPane.Core.Entities;
using TaskPane.Core.Errors;
using TaskPane.Core.Ids;

namespace TaskPane.Core.Validation;

public static class TaskHierarchyValidator
{
    /// <summary>
    /// Ensures a prospective parent assignment keeps nesting to a single child level.
    /// </summary>
    public static void EnsureCanAssignParent(
        TaskItem child,
        TaskItem? parent,
        IReadOnlyCollection<TaskItem> existingChildrenOfChild)
    {
        if (parent is null)
        {
            return;
        }

        if (parent.Id.Equals(child.Id))
        {
            throw new BusinessException(ValidationCodes.SelfParent, "A task cannot be its own parent.");
        }

        if (parent.ParentTaskId is not null)
        {
            throw new BusinessException(
                ValidationCodes.HierarchyDepth,
                "Subtasks cannot become parents. Only one child level is allowed.");
        }

        if (parent.IsDeleted)
        {
            throw new BusinessException(ValidationCodes.TaskNotFound, "Parent task is not available.");
        }

        if (existingChildrenOfChild.Count > 0)
        {
            throw new BusinessException(
                ValidationCodes.ParentHasChildren,
                "A parent with children cannot be moved under another parent.");
        }
    }

    public static void EnsureCanBecomeParent(TaskItem prospectiveParent)
    {
        if (prospectiveParent.ParentTaskId is not null)
        {
            throw new BusinessException(
                ValidationCodes.ParentIsChild,
                "A subtask cannot become a parent.");
        }
    }

    public static void EnsureExists(TaskItem? task, TaskId id)
    {
        if (task is null || task.IsDeleted)
        {
            throw new BusinessException(ValidationCodes.TaskNotFound, $"Task '{id}' was not found.");
        }
    }
}
