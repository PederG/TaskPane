using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskPane.Application.Models;
using TaskPane.Application.Services;
using TaskPane.Core.Enums;

namespace TaskPane.App.ViewModels;

public partial class QuickCaptureViewModel : ObservableObject
{
    private readonly TaskService _taskService;

    public QuickCaptureViewModel(TaskService taskService)
    {
        _taskService = taskService;
    }

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public event Action? CloseRequested;
    public event Action? Captured;

    [RelayCommand]
    private async Task CaptureAsync()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            return;
        }

        await _taskService.CreateAsync(new CreateTaskRequest
        {
            Title = Title,
            FocusBucket = FocusBucket.Inbox
        }).ConfigureAwait(true);

        Title = string.Empty;
        StatusMessage = string.Empty;
        Captured?.Invoke();
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        Title = string.Empty;
        CloseRequested?.Invoke();
    }
}
