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
    public const string InvalidDeadline = "invalid_deadline";
    public const string InvalidSleep = "invalid_sleep";
    public const string TagNameRequired = "tag_name_required";
    public const string TagDuplicate = "tag_duplicate";
    public const string ProjectNameRequired = "project_name_required";
    public const string ProjectNotFound = "project_not_found";
    public const string TagNotFound = "tag_not_found";
    public const string HotkeyConflict = "hotkey_conflict";
    public const string SessionNotFound = "session_not_found";
    public const string SessionNotOpen = "session_not_open";
    public const string SessionOverlap = "session_overlap";
    public const string InvalidSessionRange = "invalid_session_range";
    public const string IdleThresholdInvalid = "idle_threshold_invalid";
    public const string DependencySelfEdge = "dependency_self_edge";
    public const string DependencyCycle = "dependency_cycle";
    public const string DependencyDuplicate = "dependency_duplicate";
    public const string DependencyNotFound = "dependency_not_found";
    public const string AuditNotFound = "audit_not_found";
    public const string UndoNotSupported = "undo_not_supported";
    public const string UndoAlreadyApplied = "undo_already_applied";
}
