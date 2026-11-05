using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TaskPane.App.ViewModels;
using Wpf.Ui.Controls;

namespace TaskPane.App.Windows;

public partial class ManagerWindow : FluentWindow
{
    private readonly ManagerViewModel _viewModel;
    private System.Windows.Point _dragStart;
    private TaskRowViewModel? _dragged;

    public ManagerWindow(ManagerViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        // Watch with explicit Mica: system theme changes re-apply backdrop on this FluentWindow
        // only (UpdateBackground on the observed HWND). Global Apply still uses None so the
        // compact overlay stays free of native backdrop; MainWindow is pinned to Manager.
        Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this, Wpf.Ui.Controls.WindowBackdropType.Mica);
    }

    public Task InitializeAsync() => _viewModel.InitializeAsync();

    private void NewTaskBox_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _viewModel.CreateTaskCommand.CanExecute(null))
        {
            _viewModel.CreateTaskCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void TaskList_OnPreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragged is null)
        {
            if (e.LeftButton == MouseButtonState.Pressed && sender is System.Windows.Controls.ListBox list)
            {
                var pos = e.GetPosition(list);
                if ((pos - _dragStart).Length > 8 && list.SelectedItem is TaskRowViewModel row)
                {
                    _dragged = row;
                    DragDrop.DoDragDrop(list, row, System.Windows.DragDropEffects.Move);
                    _dragged = null;
                }
            }

            return;
        }
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject)
        {
            _dragStart = e.GetPosition(TaskList);
        }

        base.OnPreviewMouseLeftButtonDown(e);
    }

    private void TaskList_OnDragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(TaskRowViewModel))
            ? System.Windows.DragDropEffects.Move
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private async void TaskList_OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(TaskRowViewModel))
            || e.Data.GetData(typeof(TaskRowViewModel)) is not TaskRowViewModel source)
        {
            return;
        }

        var target = GetRowAt(e.GetPosition(TaskList));
        if (target is null || target.Id.Equals(source.Id))
        {
            return;
        }

        try
        {
            if (!source.IsSubtask && target.IsSubtask)
            {
                // Dropping a root onto a subtask is ignored (would create grandchildren).
                return;
            }

            if (source.IsSubtask && !target.IsSubtask)
            {
                await _viewModel.DropMoveToParentAsync(source, target).ConfigureAwait(true);
                return;
            }

            await _viewModel.DropReorderAsync(source, target).ConfigureAwait(true);
        }
        catch
        {
            // Hierarchy violations surface via service; keep UI responsive.
        }
    }

    private TaskRowViewModel? GetRowAt(System.Windows.Point position)
    {
        var element = TaskList.InputHitTest(position) as DependencyObject;
        while (element is not null)
        {
            if (element is ListBoxItem item)
            {
                return item.DataContext as TaskRowViewModel;
            }

            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        }

        return null;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        _viewModel.ReturnToOverlayCommand.Execute(null);
        base.OnClosing(e);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
            _viewModel.ReturnToOverlayCommand.Execute(null);
        }
    }
}
