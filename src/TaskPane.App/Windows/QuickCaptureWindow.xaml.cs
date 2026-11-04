using System.Windows;
using System.Windows.Input;
using TaskPane.App.ViewModels;

namespace TaskPane.App.Windows;

public partial class QuickCaptureWindow : Window
{
    private readonly QuickCaptureViewModel _viewModel;

    public QuickCaptureWindow(QuickCaptureViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.CloseRequested += () =>
        {
            Hide();
            TitleBox.Clear();
        };
    }

    public void ShowCapture()
    {
        _viewModel.Title = string.Empty;
        Show();
        Activate();
        TitleBox.Focus();
        Keyboard.Focus(TitleBox);
    }

    private void TitleBox_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _viewModel.CaptureCommand.CanExecute(null))
        {
            _viewModel.CaptureCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _viewModel.CancelCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void QuickCaptureWindow_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _viewModel.CancelCommand.Execute(null);
            e.Handled = true;
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }
}
