namespace TaskPane.Core.Entities;

public sealed class TimeTrackingSettings
{
    public const string SettingsKey = "time.tracking";

    /// <summary>Default inactivity threshold before idle prompt (minutes).</summary>
    public int IdleThresholdMinutes { get; set; } = 10;

    /// <summary>Task id to offer resume for after lock/suspend/idle (GUID string).</summary>
    public string? PendingResumeTaskId { get; set; }

    /// <summary>Why tracking stopped automatically (Locked/Suspended/Idle).</summary>
    public string? PendingResumeReason { get; set; }
}
