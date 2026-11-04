using Microsoft.Win32;
using TaskPane.Core.Interfaces;

namespace TaskPane.App.Services;

/// <summary>
/// Per-user autostart via HKCU Run. Never touches HKLM and never requires admin.
/// </summary>
public sealed class WindowsAutostartService : IAutostartService
{
    public const string RunValueName = "TaskPane";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public bool IsSupported => OperatingSystem.IsWindows();

    public bool IsEnabled()
    {
        if (!IsSupported)
        {
            return false;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var value = key?.GetValue(RunValueName) as string;
            return !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (!IsSupported)
        {
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                return;
            }

            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exe))
                {
                    exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                }

                if (string.IsNullOrWhiteSpace(exe))
                {
                    return;
                }

                key.SetValue(RunValueName, QuoteIfNeeded(exe));
            }
            else if (key.GetValue(RunValueName) is not null)
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Preference remains in SQLite; tray/manager can surface failure via status.
        }
    }

    private static string QuoteIfNeeded(string path)
        => path.Contains(' ', StringComparison.Ordinal) ? $"\"{path}\"" : path;
}
