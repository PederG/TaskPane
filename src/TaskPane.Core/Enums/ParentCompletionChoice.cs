namespace TaskPane.Core.Enums;

/// <summary>User decision when completing a parent that still has open children.</summary>
public enum ParentCompletionChoice
{
    Cancel = 0,
    CompleteParentOnly = 1,
    CompleteParentAndChildren = 2
}
