using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskPane.Application.Models;
using TaskPane.Application.Services;
using TaskPane.Core.Enums;

namespace TaskPane.App.ViewModels;

public partial class ManagerViewModel : ObservableObject
{
    private readonly TaskService _taskService;

    public ManagerViewModel(TaskService taskService)
    {
        _taskService = taskService;
    }

    public ObservableCollection<TaskRowViewModel> Tasks { get; } = [];
    public ObservableCollection<TaskRowViewModel> DeletedTasks { get; } = [];

    [ObservableProperty]
    private FocusBucket _selectedFocus = FocusBucket.Inbox;

    [ObservableProperty]
    private TaskRowViewModel? _selectedTask;

    [ObservableProperty]
    private string _editTitle = string.Empty;

    [ObservableProperty]
    private string _editNotes = string.Empty;

    [ObservableProperty]
    private TaskPriority _editPriority = TaskPriority.Normal;

    [ObservableProperty]
    private FocusBucket _editFocus = FocusBucket.Inbox;

    [ObservableProperty]
    private string _newTaskTitle = string.Empty;

    [ObservableProperty]
    private bool _showingRecycleBin;

    public event Action? ReturnToOverlayRequested;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
        => await RefreshAsync(cancellationToken).ConfigureAwait(true);

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (ShowingRecycleBin)
        {
            DeletedTasks.Clear();
            var deleted = await _taskService.ListDeletedAsync(cancellationToken).ConfigureAwait(true);
            foreach (var item in deleted)
            {
                DeletedTasks.Add(new TaskRowViewModel(item));
            }

            return;
        }

        Tasks.Clear();
        var roots = await _taskService.ListVisibleRootsAsync(SelectedFocus, cancellationToken).ConfigureAwait(true);
        foreach (var root in roots)
        {
            var progress = await _taskService.GetProgressAsync(root.Id, cancellationToken).ConfigureAwait(true);
            Tasks.Add(new TaskRowViewModel(root, progress, isExpanded: true));
            var children = await _taskService.ListChildrenAsync(root.Id, cancellationToken).ConfigureAwait(true);
            foreach (var child in children)
            {
                Tasks.Add(new TaskRowViewModel(child));
            }
        }
    }

    partial void OnSelectedFocusChanged(FocusBucket value)
    {
        ShowingRecycleBin = false;
        _ = RefreshAsync();
    }

    partial void OnShowingRecycleBinChanged(bool value) => _ = RefreshAsync();

    partial void OnSelectedTaskChanged(TaskRowViewModel? value)
    {
        if (value is null)
        {
            EditTitle = string.Empty;
            EditNotes = string.Empty;
            return;
        }

        EditTitle = value.Title;
        EditNotes = value.Notes ?? string.Empty;
        EditPriority = value.Priority;
        EditFocus = value.FocusBucket;
    }

    [RelayCommand]
    private async Task CreateTaskAsync()
    {
        if (string.IsNullOrWhiteSpace(NewTaskTitle))
        {
            return;
        }

        await _taskService.CreateAsync(new CreateTaskRequest
        {
            Title = NewTaskTitle,
            FocusBucket = SelectedFocus
        }).ConfigureAwait(true);

        NewTaskTitle = string.Empty;
        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task SaveSelectedAsync()
    {
        if (SelectedTask is null)
        {
            return;
        }

        await _taskService.UpdateAsync(new UpdateTaskRequest
        {
            Id = SelectedTask.Id,
            Title = EditTitle,
            Notes = string.IsNullOrWhiteSpace(EditNotes) ? null : EditNotes,
            Priority = EditPriority,
            FocusBucket = EditFocus
        }).ConfigureAwait(true);

        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task CompleteSelectedAsync()
    {
        if (SelectedTask is null)
        {
            return;
        }

        var result = await _taskService.CompleteAsync(SelectedTask.Id, null).ConfigureAwait(true);
        if (!result.Completed && result.IncompleteChildCount > 0)
        {
            await _taskService.CompleteAsync(SelectedTask.Id, ParentCompletionChoice.CompleteParentOnly)
                .ConfigureAwait(true);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task SoftDeleteSelectedAsync()
    {
        if (SelectedTask is null)
        {
            return;
        }

        await _taskService.SoftDeleteAsync(SelectedTask.Id).ConfigureAwait(true);
        SelectedTask = null;
        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RestoreDeletedAsync(TaskRowViewModel? row)
    {
        if (row?.DeletionBatchId is not { } batch)
        {
            return;
        }

        await _taskService.RestoreBatchAsync(batch).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task AddSubtaskToSelectedAsync()
    {
        if (SelectedTask is null || SelectedTask.IsSubtask)
        {
            return;
        }

        await _taskService.CreateAsync(new CreateTaskRequest
        {
            Title = "New subtask",
            ParentTaskId = SelectedTask.Id
        }).ConfigureAwait(true);

        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void ReturnToOverlay() => ReturnToOverlayRequested?.Invoke();

    [RelayCommand]
    private void ShowRecycleBin()
    {
        ShowingRecycleBin = true;
    }

    [RelayCommand]
    private void ShowTasks()
    {
        ShowingRecycleBin = false;
    }
}
