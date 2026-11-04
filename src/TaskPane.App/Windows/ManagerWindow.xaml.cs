using System.Windows;
using System.Windows.Input;
using TaskPane.App.ViewModels;

namespace TaskPane.App.Windows;

public partial class ManagerWindow : Window
{
    private readonly ManagerViewModel _viewModel;

    public ManagerWindow(ManagerViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
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

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Close/minimize manager returns to overlay; does not quit the app.
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
