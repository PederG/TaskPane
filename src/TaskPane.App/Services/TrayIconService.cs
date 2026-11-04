using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace TaskPane.App.Services;

public sealed class TrayIconService : IDisposable
{
    private NotifyIcon? _notifyIcon;

    public event Action? ShowOverlayRequested;
    public event Action? OpenManagerRequested;
    public event Action? QuitRequested;

    public void Initialize()
    {
        _notifyIcon = new NotifyIcon
        {
            Text = "TaskPane",
            Visible = true,
            Icon = System.Drawing.SystemIcons.Application
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Show overlay", null, (_, _) => ShowOverlayRequested?.Invoke());
        menu.Items.Add("Open manager", null, (_, _) => OpenManagerRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => QuitRequested?.Invoke());
        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (_, _) => ShowOverlayRequested?.Invoke();
    }

    public void Dispose()
    {
        if (_notifyIcon is null)
        {
            return;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _notifyIcon = null;
    }

    public static void InvokeOnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            action();
            return;
        }

        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }
}
