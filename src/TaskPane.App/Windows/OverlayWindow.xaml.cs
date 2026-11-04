using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using TaskPane.App.Services;
using TaskPane.App.ViewModels;
using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using TaskPane.Core.Placement;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfMenuItem = System.Windows.Controls.MenuItem;

namespace TaskPane.App.Windows;

public partial class OverlayWindow : Window
{
    private readonly OverlayViewModel _viewModel;
    private readonly DispatcherTimer _elapsedTimer;
    private CancellationTokenSource? _placementSaveCts;
    private bool _suppressPlacementSave;

    public OverlayWindow(OverlayViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.ConfirmParentCompletion = ConfirmParentCompletionAsync;

        _elapsedTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _elapsedTimer.Tick += (_, _) => _viewModel.TickActiveElapsed();
        _elapsedTimer.Start();
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
