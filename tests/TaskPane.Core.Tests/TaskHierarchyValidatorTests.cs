using TaskPane.Core.Entities;
using TaskPane.Core.Errors;
using TaskPane.Core.Ids;
using TaskPane.Core.Validation;

namespace TaskPane.Core.Tests;

public sealed class TaskHierarchyValidatorTests
{
    [Fact]
    public void Rejects_grandchild_parent_assignment()
    {
        var child = Root("child");
        var parent = Root("parent");
        parent.ParentTaskId = TaskId.New();

        var ex = Assert.Throws<BusinessException>(() =>
            TaskHierarchyValidator.EnsureCanAssignParent(child, parent, []));

        Assert.Equal(ValidationCodes.HierarchyDepth, ex.Code);
    }

    [Fact]
    public void Rejects_self_parent()
    {
        var task = Root("self");
        var ex = Assert.Throws<BusinessException>(() =>
            TaskHierarchyValidator.EnsureCanAssignParent(task, task, []));
        Assert.Equal(ValidationCodes.SelfParent, ex.Code);
    }

    [Fact]
    public void Rejects_moving_parent_with_children_under_another_parent()
    {
        var moving = Root("moving");
        var target = Root("target");
        var existingChild = Root("kid");
        existingChild.ParentTaskId = moving.Id;

        var ex = Assert.Throws<BusinessException>(() =>
            TaskHierarchyValidator.EnsureCanAssignParent(moving, target, [existingChild]));

        Assert.Equal(ValidationCodes.ParentHasChildren, ex.Code);
    }

    [Fact]
    public void Allows_one_level_child()
    {
        var child = Root("child");
        var parent = Root("parent");
        TaskHierarchyValidator.EnsureCanAssignParent(child, parent, []);
        TaskHierarchyValidator.EnsureCanBecomeParent(parent);
    }

    [Fact]
    public void Title_rejects_whitespace()
    {
        var ex = Assert.Throws<BusinessException>(() => TaskTitle.NormalizeOrThrow("  "));
        Assert.Equal(ValidationCodes.TitleRequired, ex.Code);
    }

    private static TaskItem Root(string title) => new()
    {
        Id = TaskId.New(),
        Title = title,
        CreatedUtc = DateTimeOffset.UtcNow,
        UpdatedUtc = DateTimeOffset.UtcNow,
        ManualSortKey = 1000
    };
}
