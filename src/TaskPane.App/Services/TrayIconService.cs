using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace TaskPane.App.Services;

public sealed class TrayIconService : IDisposable
{
    private NotifyIcon? _notifyIcon;

    public event Action? ShowOverlayRequested;
    public event Action? OpenManagerRequested;
    public event Action? ShowQuickCaptureRequested;
    public event Action? QuitRequested;
    public event Action? BalloonTipClicked;

    public void Initialize()
    {
        _notifyIcon = new NotifyIcon
        {
            Text = "TaskPane — offline task overlay",
            Visible = true,
            Icon = System.Drawing.SystemIcons.Application,
            BalloonTipTitle = "TaskPane"
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Show overlay", null, (_, _) => ShowOverlayRequested?.Invoke());
        menu.Items.Add("Open manager / settings", null, (_, _) => OpenManagerRequested?.Invoke());
        menu.Items.Add("Quick capture", null, (_, _) => ShowQuickCaptureRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit TaskPane", null, (_, _) => QuitRequested?.Invoke());
        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (_, _) => ShowOverlayRequested?.Invoke();
        _notifyIcon.BalloonTipClicked += (_, _) => BalloonTipClicked?.Invoke();
        _notifyIcon.MouseMove += (_, _) =>
        {
            // Keep tooltip short and free of task notes.
            if (_notifyIcon is not null && _notifyIcon.Text.Length > 60)
            {
                _notifyIcon.Text = "TaskPane";
            }
        };
    }

    /// <summary>Updates the tray tooltip. Callers must not pass task titles or notes.</summary>
    public void SetStatusTooltip(string status)
    {
        if (_notifyIcon is null)
        {
            return;
        }

        var text = string.IsNullOrWhiteSpace(status) ? "TaskPane" : $"TaskPane — {status.Trim()}";
        if (text.Length > 63)
        {
            text = text[..60] + "...";
        }

        _notifyIcon.Text = text;
    }

    public void ShowBalloon(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
    {
        if (_notifyIcon is null)
        {
            return;
        }

        // Title only — callers must not pass task notes.
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = text;
        _notifyIcon.BalloonTipIcon = icon;
        _notifyIcon.ShowBalloonTip(5000);
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
