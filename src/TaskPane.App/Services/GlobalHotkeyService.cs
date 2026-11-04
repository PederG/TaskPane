using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;
using TaskPane.Application.Services;
using TaskPane.Core.Entities;
using TaskPane.Core.Errors;

namespace TaskPane.App.Services;

/// <summary>Registers a Win32 global hotkey for quick capture. Reports conflicts without crashing.</summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int HotkeyId = 0x54_50; // 'TP'
    private const int WmHotkey = 0x0312;

    private readonly HotkeySettingsService _settings;
    private readonly ILogger<GlobalHotkeyService> _logger;
    private HwndSource? _source;
    private Window? _host;
    private bool _registered;
    private HotkeySetting _current = new();

    public GlobalHotkeyService(HotkeySettingsService settings, ILogger<GlobalHotkeyService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public event Action? HotkeyPressed;

    public HotkeySetting Current => _current;

    public string? LastError { get; private set; }

    public async Task InitializeAsync(Window hostWindow, CancellationToken cancellationToken = default)
    {
        _host = hostWindow;
        _current = await _settings.GetAsync(cancellationToken).ConfigureAwait(true);
        AttachHook(hostWindow);
        TryRegister(_current);
    }

    public async Task<bool> ApplyAsync(HotkeySetting setting, CancellationToken cancellationToken = default)
    {
        Unregister();
        _current = setting;
        await _settings.SaveAsync(setting, cancellationToken).ConfigureAwait(true);
        if (!setting.Enabled)
        {
            LastError = null;
            return true;
        }

        var ok = TryRegister(setting);
        if (!ok)
        {
            throw new BusinessException(
                ValidationCodes.HotkeyConflict,
                $"Hotkey '{setting.DisplayLabel}' is unavailable. Choose another shortcut.");
        }

        return true;
    }

    public void Dispose()
    {
        Unregister();
        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source = null;
        }
    }

    private void AttachHook(Window window)
    {
        var helper = new WindowInteropHelper(window);
        helper.EnsureHandle();
        _source = HwndSource.FromHwnd(helper.Handle);
        _source?.AddHook(WndProc);
    }

    private bool TryRegister(HotkeySetting setting)
    {
        LastError = null;
        if (_source is null || !setting.Enabled)
        {
            return false;
        }

        if (!RegisterHotKey(_source.Handle, HotkeyId, (uint)setting.Modifiers, (uint)setting.VirtualKey))
        {
            LastError = $"Could not register hotkey {setting.DisplayLabel} (conflict or unsupported).";
            _logger.LogWarning("Global hotkey registration failed for {Label}", setting.DisplayLabel);
            _registered = false;
            return false;
        }

        _registered = true;
        _logger.LogInformation("Registered global hotkey {Label}", setting.DisplayLabel);
        return true;
    }

    private void Unregister()
    {
        if (!_registered || _source is null)
        {
            return;
        }

        UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            HotkeyPressed?.Invoke();
            handled = true;
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
