namespace TaskPane.Core.Interop;

/// <summary>
/// Unpacks a Win32 LPARAM that packs signed 16-bit screen X/Y (e.g. WM_NCHITTEST).
/// Prefer <see cref="Unpack(long, out int, out int)"/> over <c>IntPtr.ToInt32()</c> on 64-bit.
/// </summary>
public static class PackedLParamPoint
{
    public static void Unpack(long lParam, out int x, out int y)
    {
        x = (short)(lParam & 0xFFFF);
        y = (short)((lParam >> 16) & 0xFFFF);
    }
}
