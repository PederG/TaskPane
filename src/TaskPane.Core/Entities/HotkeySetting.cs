namespace TaskPane.Core.Entities;

/// <summary>Persisted global quick-capture hotkey. Modifiers use Win32 MOD_* bit flags.</summary>
public sealed class HotkeySetting
{
    public const string SettingsKey = "hotkey.quickCapture";

    /// <summary>Default: Ctrl+Alt+Space.</summary>
    public int Modifiers { get; set; } = 0x0002 | 0x0001; // MOD_CONTROL | MOD_ALT

    /// <summary>Virtual-key code; default Space (0x20).</summary>
    public int VirtualKey { get; set; } = 0x20;

    public bool Enabled { get; set; } = true;

    public string DisplayLabel { get; set; } = "Ctrl+Alt+Space";
}
