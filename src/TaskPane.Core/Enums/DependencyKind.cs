namespace TaskPane.Core.Enums;

public enum DependencyKind
{
    /// <summary>Finish-to-start: successor is blocked until predecessor completes.</summary>
    Required = 0,

    /// <summary>Non-blocking informational relationship.</summary>
    Related = 1
}
