namespace TaskPane.Core.Errors;

public static class ValidationCodes
{
    public const string TitleRequired = "title_required";
    public const string TaskNotFound = "task_not_found";
    public const string HierarchyDepth = "hierarchy_depth";
    public const string SelfParent = "self_parent";
    public const string ParentIsChild = "parent_is_child";
    public const string ParentHasChildren = "parent_has_children";
    public const string IncompleteChildren = "incomplete_children";
    public const string AlreadyDeleted = "already_deleted";
    public const string NotDeleted = "not_deleted";
    public const string InvalidOrder = "invalid_order";
}
