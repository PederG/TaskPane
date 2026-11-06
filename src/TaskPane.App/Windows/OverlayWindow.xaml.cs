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
using TaskPane.Core.Ids;
using TaskPane.Core.Interop;
using TaskPane.Core.Overlay;
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

    /// <summary>DWMWINDOWATTRIBUTE — suppress the native DWM window border color (Win11+).</summary>
    private const int DwmwaBorderColor = 34;

    /// <summary>DWMWA_COLOR_NONE — no visible DWM border color.</summary>
    private const uint DwmwaColorNone = 0xFFFFFFFE;

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

    /// <summary>
    /// Local diagnostic only: set true before launch to zero RootSurface BorderThickness so
    /// Windows QA can tell a #FFFFFF left/top hairline (HWND host) from the WPF border stroke.
    /// Shipping stays false — the 1px left/top region inset addresses the host hairline.
    /// </summary>
    private bool _diagnosticZeroRootBorderThickness;

    public OverlayWindow(OverlayViewModel viewModel, ILogger<OverlayWindow> logger)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _logger = logger;
        DataContext = viewModel;
        _viewModel.ConfirmParentCompletion = ConfirmParentCompletionAsync;
        // Layered transparent host must stay fully opaque (Window.Opacity = 1). RootSurface
        // uses solid theme brushes — no user opacity / brush-alpha scaling.
        Opacity = 1;
        Background = MediaBrushes.Transparent;
        // Flip to true locally for Windows QA (WPF border vs HWND #FFFFFF hairline). Do not ship true.
        _diagnosticZeroRootBorderThickness = false;

        _viewModel.PropertyChanged += (_, e) =>
        {
            // Mini ↔ full chrome changes RootSurface size; refresh SetWindowRgn clip.
            if (e.PropertyName is nameof(OverlayViewModel.IsMiniSessionChrome)
                or nameof(OverlayViewModel.OverlayMinHeight)
                or null)
            {
                MinHeight = _viewModel.OverlayMinHeight;
                ScheduleNativeRoundedRegionUpdate();
            }
        };

        _elapsedTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _elapsedTimer.Tick += (_, _) =>
        {
            _viewModel.TickActiveElapsed();
            _ = _viewModel.CheckAutoWakeAsync();
        };
        _elapsedTimer.Start();
        _viewModel.PromptCustomSleepUntil = PromptCustomSleepUntil;
        _viewModel.PromptPredecessor = PromptPredecessor;

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
        if (_diagnosticZeroRootBorderThickness)
        {
            RootSurface.BorderThickness = new Thickness(0);
        }

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
    /// layered host outside RootSurface (incl. 1px left/top inset for the host hairline).
    /// SetWindowRgn owns the HRGN on success.
    /// <para>
    /// Corner AA tradeoff: <c>CreateRoundRectRgn</c> is binary — it cannot antialias, and a
    /// tight region clips WPF's sub-pixel CornerRadius fringe against the desktop. Expanding
    /// the region outward would expose the white HWND host in the corner wedges (the defect
    /// SetWindowRgn exists to hide). No shipping expand/offset/radius trial constants.
    /// Prefer layered per-pixel alpha for a smooth outer silhouette; keep this region as the
    /// white-host fallback until Windows QA verifies an alpha path. No fake outlines/halos.
    /// </para>
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

        // PointToScreen returns screen coords already consistent with GetWindowRect — do not
        // multiply by DpiScale again (only the DIP radius is scaled in Compute).
        WpfPoint rootTopLeftScreen;
        WpfPoint rootBottomRightScreen;
        try
        {
            rootTopLeftScreen = RootSurface.PointToScreen(new WpfPoint(0, 0));
            rootBottomRightScreen = RootSurface.PointToScreen(
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
            rootTopLeftScreen.X,
            rootTopLeftScreen.Y,
            rootBottomRightScreen.X,
            rootBottomRightScreen.Y,
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

        ResolveVisibleRootSurfaceBounds(out var visL, out var visT, out var visR, out var visB);
        var hit = OverlayNcHitTest.Decide(
            local.X,
            local.Y,
            width,
            height,
            OverlayNcHitTest.DefaultResizeBorderDip,
            OverlayNcHitTest.DefaultCornerHitDip,
            ResolveTopCaptionBandDip(),
            positionLocked: _viewModel.Placement.PositionLocked,
            overInteractiveChrome: IsOverHeaderActionChrome(local),
            visibleLeft: visL,
            visibleTop: visT,
            visibleRight: visR,
            visibleBottom: visB);

        handled = true;
        return new IntPtr(hit);
    }

    private double ResolveTopCaptionBandDip()
    {
        if (TryFindResource("TpOverlayPad") is double pad)
        {
            return pad;
        }

        if (TryFindResource("TpOverlayPadThickness") is Thickness thickness)
        {
            return thickness.Top;
        }

        return OverlayNcHitTest.DefaultTopCaptionBandDip;
    }

    private void ResolveVisibleRootSurfaceBounds(
        out double left,
        out double top,
        out double right,
        out double bottom)
    {
        left = 0;
        top = 0;
        right = ActualWidth;
        bottom = ActualHeight;

        if (RootSurface.ActualWidth <= 0 || RootSurface.ActualHeight <= 0)
        {
            return;
        }

        try
        {
            var topLeft = RootSurface.TransformToAncestor(this).Transform(new WpfPoint(0, 0));
            left = topLeft.X;
            top = topLeft.Y;
            right = topLeft.X + RootSurface.ActualWidth;
            bottom = topLeft.Y + RootSurface.ActualHeight;
        }
        catch (InvalidOperationException)
        {
            // Presentation source not ready — fall back to full window.
        }
    }

    /// <summary>
    /// True when the point is over Search / Expand / More inside <see cref="DragHeader"/>,
    /// or over the mini-strip pause button. Those must stay HTCLIENT so HTCAPTION never
    /// steals their clicks.
    /// </summary>
    private bool IsOverHeaderActionChrome(WpfPoint local)
    {
        HitTestResult? result;
        try
        {
            result = VisualTreeHelper.HitTest(this, local);
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        if (result?.VisualHit is not DependencyObject hit)
        {
            return false;
        }

        System.Windows.Controls.Button? button = null;
        for (var current = hit; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is System.Windows.Controls.Button found)
            {
                button = found;
            }

            if (ReferenceEquals(current, MiniPauseButton))
            {
                return true;
            }

            if (ReferenceEquals(current, DragHeader))
            {
                return button is not null;
            }

            if (ReferenceEquals(current, this))
            {
                break;
            }
        }

        return false;
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
        // Do not persist the mini-strip height as the restored full-chrome height.
        var height = ActualHeight > 0 ? ActualHeight : Height;
        if (_viewModel.IsMiniSessionChrome)
        {
            height = Math.Max(_viewModel.Placement.Height, OverlaySessionChrome.FullMinHeightDip);
        }

        return new OverlayPlacement
        {
            Left = Left,
            Top = Top,
            Width = Width,
            Height = height,
            MaxHeight = _viewModel.MaxContentHeight,
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
        _viewModel.Topmost = placement.Topmost;
        _viewModel.MaxContentHeight = placement.MaxHeight > 0 ? placement.MaxHeight : 720;
        MaxHeight = _viewModel.MaxContentHeight;
        EnsureOnScreen();
        _suppressPlacementSave = false;
    }

    private void OverlayWindow_OnActivated(object? sender, EventArgs e)
        => _viewModel.NotifyOverlayActivated();

    private void OverlayWindow_OnDeactivated(object? sender, EventArgs e)
        => _viewModel.NotifyOverlayDeactivated();

    private void MiniSessionStrip_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Pause must keep working; anything else expands full chrome (and may drag).
        if (e.OriginalSource is DependencyObject source
            && IsInsideMiniPauseButton(source))
        {
            return;
        }

        if (_viewModel.ExpandSessionChromeCommand.CanExecute(null))
        {
            _viewModel.ExpandSessionChromeCommand.Execute(null);
        }

        if (_viewModel.Placement.PositionLocked)
        {
            return;
        }

        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private bool IsInsideMiniPauseButton(DependencyObject source)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, MiniPauseButton))
            {
                return true;
            }

            if (ReferenceEquals(current, this) || ReferenceEquals(current, MiniSessionStrip))
            {
                break;
            }
        }

        return false;
    }

    private void DragHeader_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // WPF path for the header row; empty top padding uses native HTCAPTION via WM_NCHITTEST.
        // Both paths honor position-locked.
        if (_viewModel.Placement.PositionLocked)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject source
            && IsHeaderActionButton(source))
        {
            return;
        }

        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private static bool IsHeaderActionButton(DependencyObject source)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is System.Windows.Controls.Button)
            {
                return true;
            }

            if (current is OverlayWindow)
            {
                break;
            }
        }

        return false;
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
            if (row.IsEditing)
            {
                return;
            }

            if (_viewModel.ShowNotesCommand.CanExecute(row))
            {
                _viewModel.ShowNotesCommand.Execute(row);
            }

            e.Handled = true;
        }
    }

    private void TitleEditBox_OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox box || box.Visibility != Visibility.Visible)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (box.IsVisible)
            {
                box.Focus();
                Keyboard.Focus(box);
                box.SelectAll();
            }
        }, DispatcherPriority.Input);
    }

    private void TitleEditBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TaskRowViewModel row }
            && _viewModel.CommitInlineEditCommand.CanExecute(row))
        {
            _viewModel.CommitInlineEditCommand.Execute(row);
        }
    }

    private void TitleEditBox_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TaskRowViewModel row })
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            if (_viewModel.CommitInlineEditCommand.CanExecute(row))
            {
                _viewModel.CommitInlineEditCommand.Execute(row);
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (_viewModel.CancelInlineEditCommand.CanExecute(row))
            {
                _viewModel.CancelInlineEditCommand.Execute(row);
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

    private void TaskMenuSleepPreset_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetRow(sender, out var row))
        {
            return;
        }

        var tag = (sender as WpfMenuItem)?.Tag as string;
        var preset = Enum.TryParse<SleepPreset>(tag, ignoreCase: true, out var parsed)
            ? parsed
            : SleepPreset.OneHour;
        _ = _viewModel.SleepWithPresetAsync(row, preset);
    }

    private void TaskMenuWake_OnClick(object sender, RoutedEventArgs e)
    {
        if (TryGetRow(sender, out var row) && _viewModel.WakeCommand.CanExecute(row))
        {
            _viewModel.WakeCommand.Execute(row);
        }
    }

    private DateTimeOffset? PromptCustomSleepUntil(TaskRowViewModel row)
    {
        var dialog = new SleepUntilDialog
        {
            Owner = this
        };
        return dialog.ShowDialog() == true ? dialog.SelectedUntilUtc : null;
    }

    private TaskId? PromptPredecessor(TaskRowViewModel row, IReadOnlyList<TaskRowViewModel> candidates)
    {
        var items = candidates
            .Select(c => new TaskPickerItem { Id = c.Id, Title = c.Title })
            .ToList();
        var dialog = new TaskPickerDialog(
            title: "Depends on",
            prompt: $"Choose a task that must finish before “{row.Title}” can proceed.",
            items: items)
        {
            Owner = this
        };
        return dialog.ShowDialog() == true ? dialog.SelectedItem?.Id : null;
    }

    private void TaskMenuProject_OnSubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfMenuItem projectRoot || !TryGetRow(sender, out var row))
        {
            return;
        }

        projectRoot.Items.Clear();
        var none = new WpfMenuItem
        {
            Header = "(No project)",
            IsChecked = row.ProjectId is null,
            IsCheckable = true,
            Tag = null
        };
        none.Click += TaskMenuAssignProject_OnClick;
        projectRoot.Items.Add(none);

        if (_viewModel.Projects.Count == 0)
        {
            projectRoot.Items.Add(new WpfMenuItem
            {
                Header = "No projects yet — create one in Manager",
                IsEnabled = false
            });
            return;
        }

        foreach (var project in _viewModel.Projects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            var item = new WpfMenuItem
            {
                Header = project.Name,
                IsCheckable = true,
                IsChecked = row.ProjectId is { } pid && pid.Equals(project.Id),
                Tag = project.Id
            };
            item.Click += TaskMenuAssignProject_OnClick;
            projectRoot.Items.Add(item);
        }
    }

    private void TaskMenuAssignProject_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetRow(sender, out var row))
        {
            return;
        }

        ProjectId? projectId = null;
        if (sender is WpfMenuItem { Tag: ProjectId tagged })
        {
            projectId = tagged;
        }

        _ = _viewModel.AssignProjectAsync(row, projectId);
    }

    private void TaskMenuAddBlock_OnClick(object sender, RoutedEventArgs e)
    {
        if (TryGetRow(sender, out var row))
        {
            _ = _viewModel.AddBlockAsync(row);
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
        // Nested Sleep submenu items parent through MenuItem → ContextMenu; walk up.
        DependencyObject? current = sender as DependencyObject;
        while (current is not null)
        {
            if (current is WpfContextMenu { DataContext: TaskRowViewModel fromMenu })
            {
                row = fromMenu;
                return true;
            }

            if (current is FrameworkElement { DataContext: TaskRowViewModel fromElement }
                and not WpfMenuItem and not WpfContextMenu)
            {
                row = fromElement;
                return true;
            }

            current = LogicalTreeHelper.GetParent(current)
                      ?? (current is Visual visual ? VisualTreeHelper.GetParent(visual) : null);
        }

        if (sender is FrameworkElement { DataContext: TaskRowViewModel fallback })
        {
            row = fallback;
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
