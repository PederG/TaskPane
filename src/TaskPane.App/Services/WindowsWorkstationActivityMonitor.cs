using System.Runtime.InteropServices;
using Microsoft.Win32;
using TaskPane.Core.Interfaces;

namespace TaskPane.App.Services;

/// <summary>Subscribes to Windows session lock/unlock and power suspend/resume.</summary>
public sealed class WindowsWorkstationActivityMonitor : IWorkstationActivityMonitor
{
    private bool _started;
    private System.Windows.Threading.DispatcherTimer? _idlePoll;

    public event EventHandler? SessionLocked;
    public event EventHandler? SessionUnlocked;
    public event EventHandler? SystemSuspended;
    public event EventHandler? SystemResumed;

    public event EventHandler<TimeSpan>? IdleThresholdExceeded;

    public TimeSpan IdleThreshold { get; set; } = TimeSpan.FromMinutes(10);

    private bool _idleRaised;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        _idlePoll = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _idlePoll.Tick += (_, _) => PollIdle();
        _idlePoll.Start();
    }

    public TimeSpan? GetIdleDuration()
    {
        try
        {
            var info = new LastInputInfo { CbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
            if (!GetLastInputInfo(ref info))
            {
                return null;
            }

            var elapsedMs = unchecked(Environment.TickCount - (int)info.DwTime);
            if (elapsedMs < 0)
            {
                elapsedMs = 0;
            }

            return TimeSpan.FromMilliseconds(elapsedMs);
        }
        catch
        {
            return null;
        }
    }

    private void PollIdle()
    {
        var idle = GetIdleDuration();
        if (idle is null)
        {
            return;
        }

        if (idle.Value >= IdleThreshold)
        {
            if (!_idleRaised)
            {
                _idleRaised = true;
                IdleThresholdExceeded?.Invoke(this, idle.Value);
            }
        }
        else
        {
            _idleRaised = false;
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
                SessionLocked?.Invoke(this, EventArgs.Empty);
                break;
            case SessionSwitchReason.SessionUnlock:
                SessionUnlocked?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Suspend:
                SystemSuspended?.Invoke(this, EventArgs.Empty);
                break;
            case PowerModes.Resume:
                SystemResumed?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    public void Dispose()
    {
        if (!_started)
        {
            return;
        }

        _started = false;
        if (_idlePoll is not null)
        {
            _idlePoll.Stop();
            _idlePoll = null;
        }

        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo plii);

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint CbSize;
        public uint DwTime;
    }
}
