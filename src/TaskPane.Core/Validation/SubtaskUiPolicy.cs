namespace TaskPane.Core.Validation;

/// <summary>
/// Shared overlay/manager rules for one-level subtasks (UI affordances, not persistence).
/// </summary>
public static class SubtaskUiPolicy
{
    /// <summary>Placeholder title created before the user types a real name.</summary>
    public const string NewSubtaskPlaceholderTitle = "New subtask";

    /// <summary>Left indent (DIP) so subtask rows read as children of their parent.</summary>
    public const double SubtaskIndentDip = 16;

    /// <summary>Only roots may offer "Add subtask" — nesting stops at one child level.</summary>
    public static bool CanOfferAddSubtask(bool isSubtask) => !isSubtask;
}
