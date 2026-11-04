namespace TaskPane.Core.Entities;

public sealed class ReminderSettings
{
    public const string SettingsKey = "reminders.settings";

    public bool QuietHoursEnabled { get; set; } = true;

    /// <summary>Local HH:mm. Default quiet hours 22:00–07:00.</summary>
    public string QuietStartLocal { get; set; } = "22:00";

    public string QuietEndLocal { get; set; } = "07:00";

    public int DefaultSnoozeMinutes { get; set; } = 15;
}
