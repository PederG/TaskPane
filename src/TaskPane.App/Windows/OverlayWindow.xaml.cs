using System.Windows;
using System.Windows.Input;
using TaskPane.App.ViewModels;
using TaskPane.Core.Entities;
using TaskPane.Core.Enums;

namespace TaskPane.App.Windows;

public partial class OverlayWindow : Window
{
    private readonly OverlayViewModel _viewModel;
    private CancellationTokenSource? _placementSaveCts;
    private bool _suppressPlacementSave;

    public OverlayWindow(OverlayViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.ConfirmParentCompletion = ConfirmParentCompletionAsync;
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
            Height = Height,
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
        Height = placement.Height;
        _viewModel.Opacity = placement.Opacity;
        _viewModel.Topmost = placement.Topmost;
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
        var workArea = SystemParameters.WorkArea;
        if (Width < MinWidth)
        {
            Width = MinWidth;
        }

        if (Height < MinHeight)
        {
            Height = MinHeight;
        }

        if (Left + Width < workArea.Left + 40)
        {
            Left = workArea.Left + 40;
        }

        if (Top + Height < workArea.Top + 40)
        {
            Top = workArea.Top + 40;
        }

        if (Left > workArea.Right - 40)
        {
            Left = workArea.Right - Width - 40;
        }

        if (Top > workArea.Bottom - 40)
        {
            Top = workArea.Bottom - Height - 40;
        }
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
