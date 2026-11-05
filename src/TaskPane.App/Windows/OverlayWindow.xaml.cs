using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using TaskPane.App.Services;
using TaskPane.App.ViewModels;
using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using TaskPane.Core.Interop;
using TaskPane.Core.Placement;
using MediaBrushes = System.Windows.Media.Brushes;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using WpfPoint = System.Windows.Point;
using WindowBackdrop = Wpf.Ui.Controls.WindowBackdrop;

namespace TaskPane.App.Windows;

public partial class OverlayWindow : Window
{
    private const int WmNcHitTest = 0x0084;
    private const int HtClient = 1;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;

    /// <summary>DWMWINDOWATTRIBUTE — suppress the native DWM window border color (Win11+).</summary>
    private const int DwmwaBorderColor = 34;

    /// <summary>DWMWA_COLOR_NONE — no visible DWM border color.</summary>
    private const uint DwmwaColorNone = 0xFFFFFFFE;

    /// <summary>Invisible edge hit-test thickness (DIP) for layered-window resize.</summary>
    private const double ResizeBorderDip = 6;

    /// <summary>Fallback corner radius (DIP) when TpOverlayWindowRadius is missing.</summary>
    private const double DefaultCornerRadiusDip = 18;

    private readonly OverlayViewModel _viewModel;
    private readonly ILogger<OverlayWindow> _logger;
    private readonly DispatcherTimer _elapsedTimer;
    private CancellationTokenSource? _placementSaveCts;
    private bool _suppressPlacementSave;
    private bool _nativeRegionUpdateScheduled;
    private RoundWindowRegionParams? _appliedNativeRegion;
    private IntPtr _hookedHwnd;

    public OverlayWindow(OverlayViewModel viewModel, ILogger<OverlayWindow> logger)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _logger = logger;
        DataContext = viewModel;
        _viewModel.ConfirmParentCompletion = ConfirmParentCompletionAsync;
        // Layered transparent host must stay fully opaque; content translucency is on RootSurface.
        Opacity = 1;
        Background = MediaBrushes.Transparent;

        _elapsedTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _elapsedTimer.Tick += (_, _) => _viewModel.TickActiveElapsed();
        _elapsedTimer.Start();

        DpiChanged += OverlayWindow_OnDpiChanged;
    }

    private void OverlayWindow_OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var source = HwndSource.FromHwnd(handle);
        if (source is null)
        {
            return;
        }

        // WPF layered windows default CompositionTarget.BackgroundColor to white — that paints
        // opaque rectangular corners outside the rounded RootSurface. Keep it transparent.
        if (source.CompositionTarget is not null)
        {
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
        }

        // Scrub any DWM system backdrop (Mica/Acrylic) that global theme Apply may have left on
        // this HWND. AllowsTransparency + native backdrop → opaque white corner triangles.
        IsolateFromNativeBackdrop();
        // After HWND init + backdrop scrub: hide the native rectangular DWM border outline.
        SuppressNativeDwmBorder();

        // HWND recreate (or first create): drop cached region and re-hook.
        if (_hookedHwnd != handle)
        {
            _hookedHwnd = handle;
            _appliedNativeRegion = null;
            source.AddHook(WndProc);
        }

        ScheduleNativeRoundedRegionUpdate();
    }

    private void OverlayWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        // Re-assert after theme/backdrop helpers that may restore SystemColors.WindowColor.
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero
            && HwndSource.FromHwnd(handle)?.CompositionTarget is { } compositionTarget)
        {
            compositionTarget.BackgroundColor = Colors.Transparent;
        }

        Background = MediaBrushes.Transparent;
        IsolateFromNativeBackdrop();
        SuppressNativeDwmBorder();
        ApplyRootSurfaceClip();
        // First show + layout: clip HWND to RootSurface round-rect (excludes white host corners).
        ScheduleNativeRoundedRegionUpdate();
    }

    private void OverlayWindow_OnDpiChanged(object sender, System.Windows.DpiChangedEventArgs e)
    {
        _appliedNativeRegion = null;
        ScheduleNativeRoundedRegionUpdate();
    }

    /// <summary>
    /// Compact overlay must stay a plain layered WPF window — no DWM system backdrop.
    /// If white corners persist after global Apply(None) + this scrub, next Windows check is
    /// HWND extended styles (WS_EX_LAYERED) vs a minimal AllowsTransparency probe window.
    /// </summary>
    private void IsolateFromNativeBackdrop()
    {
        _ = WindowBackdrop.RemoveBackdrop(this);
    }

    /// <summary>
    /// Hide the native DWM rectangular border (DWMWA_BORDER_COLOR = DWMWA_COLOR_NONE).
    /// Keeps <see cref="ResizeMode"/> = CanResize and custom WM_NCHITTEST edge resize.
    /// </summary>
    private void SuppressNativeDwmBorder()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var color = unchecked((int)DwmwaColorNone);
        var hr = DwmSetWindowAttribute(handle, DwmwaBorderColor, ref color, sizeof(int));
        if (hr != 0)
        {
            _logger.LogWarning(
                "DwmSetWindowAttribute(DWMWA_BORDER_COLOR=COLOR_NONE) failed: HRESULT 0x{HResult:X8}",
                hr);
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateRoundRectRgn(
        int nLeftRect,
        int nTopRect,
        int nRightRect,
        int nBottomRect,
        int nWidthEllipse,
        int nHeightEllipse);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private void RootSurface_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyRootSurfaceClip();
        ScheduleNativeRoundedRegionUpdate();
    }

    private void ApplyRootSurfaceClip()
    {
        if (RootSurface.ActualWidth <= 0 || RootSurface.ActualHeight <= 0)
        {
            return;
        }

        var radius = ResolveCornerRadiusDip();

        RootSurface.Clip = new RectangleGeometry(
            new Rect(0, 0, RootSurface.ActualWidth, RootSurface.ActualHeight),
            radius,
            radius);
    }

    private double ResolveCornerRadiusDip()
    {
        return TryFindResource("TpOverlayWindowRadius") is CornerRadius corner
            ? corner.TopLeft
            : DefaultCornerRadiusDip;
    }

    /// <summary>
    /// Coalesce region updates onto the dispatcher after layout so SizeChanged / DPI /
    /// SourceInitialized do not recurse into measure while applying SetWindowRgn.
    /// </summary>
    private void ScheduleNativeRoundedRegionUpdate()
    {
        if (_nativeRegionUpdateScheduled)
        {
            return;
        }

        _nativeRegionUpdateScheduled = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, ApplyNativeRoundedRegion);
    }

    /// <summary>
    /// Native HWND clip matching RootSurface physical bounds (rounded). Excludes the white
    /// layered host that can sit ~1px outside RootSurface. SetWindowRgn owns the HRGN on success.
    /// </summary>
    private void ApplyNativeRoundedRegion()
    {
        _nativeRegionUpdateScheduled = false;

        if (!IsVisible
            || RootSurface.ActualWidth <= 0
            || RootSurface.ActualHeight <= 0)
        {
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        if (!GetWindowRect(hwnd, out var windowRect))
        {
            _logger.LogWarning(
                "GetWindowRect failed for overlay HWND (Win32 {Error})",
                Marshal.GetLastWin32Error());
            return;
        }

        WpfPoint rootTopLeftDip;
        WpfPoint rootBottomRightDip;
        try
        {
            rootTopLeftDip = RootSurface.PointToScreen(new WpfPoint(0, 0));
            rootBottomRightDip = RootSurface.PointToScreen(
                new WpfPoint(RootSurface.ActualWidth, RootSurface.ActualHeight));
        }
        catch (InvalidOperationException)
        {
            // Not connected to a presentation source yet.
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(RootSurface);
        var region = RoundWindowRegionGeometry.Compute(
            windowRect.Left,
            windowRect.Top,
            rootTopLeftDip.X,
            rootTopLeftDip.Y,
            rootBottomRightDip.X,
            rootBottomRightDip.Y,
            dpi.DpiScaleX,
            dpi.DpiScaleY,
            ResolveCornerRadiusDip());

        if (!region.IsValid)
        {
            return;
        }

        if (_appliedNativeRegion is { } previous && previous.Equals(region))
        {
            return;
        }

        var hrgn = CreateRoundRectRgn(
            region.Left,
            region.Top,
            region.Right,
            region.Bottom,
            region.EllipseWidth,
            region.EllipseHeight);

        if (hrgn == IntPtr.Zero)
        {
            _logger.LogWarning(
                "CreateRoundRectRgn failed for overlay (Win32 {Error})",
                Marshal.GetLastWin32Error());
            return;
        }

        // SetWindowRgn returns nonzero on success and then owns hrgn (do not DeleteObject).
        if (SetWindowRgn(hwnd, hrgn, bRedraw: true) == 0)
        {
            _logger.LogWarning(
                "SetWindowRgn failed for overlay (Win32 {Error})",
                Marshal.GetLastWin32Error());
            _ = DeleteObject(hrgn);
            return;
        }

        _appliedNativeRegion = region;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmNcHitTest)
        {
            return IntPtr.Zero;
        }

        // Screen coordinates from lParam → window-local DIP for edge hit testing.
        // Use ToInt64 + signed 16-bit halves — ToInt32() is unsafe on 64-bit processes.
        PackedLParamPoint.Unpack(lParam.ToInt64(), out var screenX, out var screenY);
        WpfPoint local;
        try
        {
            local = PointFromScreen(new WpfPoint(screenX, screenY));
        }
        catch (InvalidOperationException)
        {
            return IntPtr.Zero;
        }

        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return IntPtr.Zero;
        }

        var border = ResizeBorderDip;
        var onLeft = local.X <= border;
        var onRight = local.X >= width - border;
        var onTop = local.Y <= border;
        var onBottom = local.Y >= height - border;

        var hit = (onLeft, onRight, onTop, onBottom) switch
        {
            (true, _, true, _) => HtTopLeft,
            (_, true, true, _) => HtTopRight,
            (true, _, _, true) => HtBottomLeft,
            (_, true, _, true) => HtBottomRight,
            (true, _, _, _) => HtLeft,
            (_, true, _, _) => HtRight,
            (_, _, true, _) => HtTop,
            (_, _, _, true) => HtBottom,
            _ => HtClient
        };

        handled = true;
        return new IntPtr(hit);
    }

    public async Task InitializeAsync()
    {
        _suppressPlacementSave = true;
        await _viewModel.InitializeAsync().ConfigureAwait(true);
        ApplyPlacement(_viewModel.Placement);
        EnsureOnScreen();
        _suppressPlacementSave = false;
    }

    public OverlayPlacement CapturePlacement()
    {
        return new OverlayPlacement
        {
            Left = Left,
            Top = Top,
            Width = Width,
            Height = ActualHeight > 0 ? ActualHeight : Height,
            MaxHeight = _viewModel.MaxContentHeight,
            Opacity = _viewModel.Opacity,
            Topmost = _viewModel.Topmost,
            PositionLocked = _viewModel.Placement.PositionLocked,
            MonitorDeviceName = GetMonitorName()
        };
    }

    public void ApplyPlacement(OverlayPlacement placement)
    {
        _suppressPlacementSave = true;
        Left = placement.Left;
        Top = placement.Top;
        Width = placement.Width;
        _viewModel.Opacity = placement.Opacity;
        _viewModel.Topmost = placement.Topmost;
        _viewModel.MaxContentHeight = placement.MaxHeight > 0 ? placement.MaxHeight : 720;
        MaxHeight = _viewModel.MaxContentHeight;
        EnsureOnScreen();
        _suppressPlacementSave = false;
    }

    private void DragHeader_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.Placement.PositionLocked)
        {
            return;
        }

        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void QuickAddBox_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _viewModel.QuickAddCommand.CanExecute(null))
        {
            _viewModel.QuickAddCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void TaskTitle_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TaskRowViewModel row })
        {
            if (_viewModel.ShowNotesCommand.CanExecute(row))
            {
                _viewModel.ShowNotesCommand.Execute(row);
            }

            e.Handled = true;
        }
    }

    private void MoreButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (MoreButton.ContextMenu is null)
        {
            return;
        }

        MoreButton.ContextMenu.PlacementTarget = MoreButton;
        MoreButton.ContextMenu.IsOpen = true;
    }

    private void TaskRowMenu_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { ContextMenu: { } menu } element)
        {
            menu.DataContext = element.DataContext;
            menu.PlacementTarget = element;
            menu.IsOpen = true;
        }
    }

    private void TaskMenuSleep_OnClick(object sender, RoutedEventArgs e)
    {
        if (TryGetRow(sender, out var row) && _viewModel.SleepCommand.CanExecute(row))
        {
            _viewModel.SleepCommand.Execute(row);
        }
    }

    private void TaskMenuAddSubtask_OnClick(object sender, RoutedEventArgs e)
    {
        if (TryGetRow(sender, out var row) && _viewModel.AddSubtaskCommand.CanExecute(row))
        {
            _viewModel.AddSubtaskCommand.Execute(row);
        }
    }

    private void TaskMenuExpand_OnClick(object sender, RoutedEventArgs e)
    {
        if (TryGetRow(sender, out var row) && _viewModel.ToggleExpandCommand.CanExecute(row))
        {
            _viewModel.ToggleExpandCommand.Execute(row);
        }
    }

    private void TaskMenuNotes_OnClick(object sender, RoutedEventArgs e)
    {
        if (TryGetRow(sender, out var row) && _viewModel.ShowNotesCommand.CanExecute(row))
        {
            _viewModel.ShowNotesCommand.Execute(row);
        }
    }

    private void TaskMenuDelete_OnClick(object sender, RoutedEventArgs e)
    {
        if (TryGetRow(sender, out var row) && _viewModel.SoftDeleteCommand.CanExecute(row))
        {
            _viewModel.SoftDeleteCommand.Execute(row);
        }
    }

    private static bool TryGetRow(object sender, out TaskRowViewModel row)
    {
        row = null!;
        if (sender is WpfMenuItem { Parent: WpfContextMenu { DataContext: TaskRowViewModel fromMenu } })
        {
            row = fromMenu;
            return true;
        }

        if (sender is FrameworkElement { DataContext: TaskRowViewModel fromElement })
        {
            row = fromElement;
            return true;
        }

        return false;
    }

    private void OverlayWindow_OnPlacementChanged(object? sender, EventArgs e)
    {
        // Window resize can change HWND vs RootSurface physical offset (~1px chrome).
        // LocationChanged alone keeps HWND-relative bounds; SizeChanged refreshes the region.
        if (e is SizeChangedEventArgs)
        {
            ScheduleNativeRoundedRegionUpdate();
        }

        if (_suppressPlacementSave || !IsVisible)
        {
            return;
        }

        _placementSaveCts?.Cancel();
        _placementSaveCts = new CancellationTokenSource();
        var token = _placementSaveCts.Token;
        _ = DebouncedSaveAsync(token);
    }

    private async Task DebouncedSaveAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(300, token).ConfigureAwait(true);
            await _viewModel.PersistPlacementAsync(CapturePlacement(), token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // expected while resizing/moving
        }
    }

    private void EnsureOnScreen()
    {
        var workAreas = MonitorWorkAreaProbe.GetWorkAreas();
        var deviceNames = MonitorWorkAreaProbe.GetDeviceNames();
        var height = ActualHeight > 0 ? ActualHeight : Math.Min(Height, MaxHeight > 0 ? MaxHeight : 720);
        var current = new WindowPlacementCalculator.Rect(Left, Top, Width, height);
        var preferredIndex = WindowPlacementCalculator.FindPreferredWorkAreaIndex(
            current,
            workAreas,
            _viewModel.Placement.MonitorDeviceName,
            deviceNames);

        var restored = WindowPlacementCalculator.EnsureVisible(
            current,
            workAreas,
            MinWidth,
            MinHeight,
            MaxWidth > 0 ? MaxWidth : 900,
            MaxHeight > 0 ? MaxHeight : 1200,
            edgeMargin: 40,
            preferredWorkAreaIndex: preferredIndex);

        Left = restored.Left;
        Top = restored.Top;
        Width = restored.Width;
        // Height is SizeToContent — do not force restored height.
    }

    private static string? GetMonitorName()
    {
        try
        {
            return System.Windows.Forms.Screen.FromPoint(
                new System.Drawing.Point((int)System.Windows.Forms.Cursor.Position.X, (int)System.Windows.Forms.Cursor.Position.Y))
                .DeviceName;
        }
        catch
        {
            return null;
        }
    }

    private static Task<ParentCompletionChoice> ConfirmParentCompletionAsync(string message, int incompleteCount)
    {
        var result = System.Windows.MessageBox.Show(
            $"{message}\n\nYes = complete parent and children\nNo = complete parent only\nCancel = leave unchanged",
            "Complete parent task",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        return Task.FromResult(result switch
        {
            MessageBoxResult.Yes => ParentCompletionChoice.CompleteParentAndChildren,
            MessageBoxResult.No => ParentCompletionChoice.CompleteParentOnly,
            _ => ParentCompletionChoice.Cancel
        });
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Closing overlay should not quit; hide instead. Explicit Quit ends the process.
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }
}
