using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskPane.Application.Models;
using TaskPane.Application.Services;
using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using TaskPane.Core.Ids;
using TaskPane.Core.Time;

namespace TaskPane.App.ViewModels;

public partial class OverlayViewModel : ObservableObject
{
    private readonly TaskService _taskService;
    private readonly OverlaySettingsService _overlaySettings;
    private readonly TimeTrackingService _timeTracking;
    private readonly HashSet<TaskId> _expanded = [];

    public OverlayViewModel(
        TaskService taskService,
        OverlaySettingsService overlaySettings,
        TimeTrackingService timeTracking)
    {
        _taskService = taskService;
        _overlaySettings = overlaySettings;
        _timeTracking = timeTracking;
    }

    public ObservableCollection<TaskRowViewModel> VisibleTasks { get; } = [];
    public ObservableCollection<TaskRowViewModel> SleepingTasks { get; } = [];

    [ObservableProperty]
    private FocusBucket _selectedFocus = FocusBucket.Today;

    [ObservableProperty]
    private string _quickAddText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private double _opacity = 0.92;

    [ObservableProperty]
    private bool _topmost = true;

    [ObservableProperty]
    private OverlayPlacement _placement = new();

    [ObservableProperty]
    private TaskRowViewModel? _notePreviewTask;

    [ObservableProperty]
    private bool _notePopoverOpen;

    [ObservableProperty]
    private bool _showSleeping;

    [ObservableProperty]
    private bool _hasSleepingTasks;

    public event Action? OpenManagerRequested;
    public event Action? QuitRequested;
    public Func<string, int, Task<ParentCompletionChoice>>? ConfirmParentCompletion { get; set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Placement = await _overlaySettings.GetAsync(cancellationToken).ConfigureAwait(true);
        Opacity = Placement.Opacity;
        Topmost = Placement.Topmost;
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var active = await _timeTracking.GetActiveSessionAsync(cancellationToken).ConfigureAwait(true);
        var roots = await _taskService.ListVisibleRootsAsync(SelectedFocus, cancellationToken).ConfigureAwait(true);
        VisibleTasks.Clear();

        foreach (var root in roots)
        {
            var progress = await _taskService.GetProgressAsync(root.Id, cancellationToken).ConfigureAwait(true);
            var expanded = _expanded.Contains(root.Id);
            var row = new TaskRowViewModel(root, progress, expanded);
            await ApplyTimeAsync(row, active, cancellationToken).ConfigureAwait(true);
            VisibleTasks.Add(row);

            if (!expanded)
            {
                continue;
            }

            var children = await _taskService.QueryChildrenAsync(
                root.Id,
                new TaskQuery { OverlayVisibility = true, FocusBucket = SelectedFocus },
                cancellationToken).ConfigureAwait(true);
            foreach (var child in children)
            {
                var childRow = new TaskRowViewModel(child);
                await ApplyTimeAsync(childRow, active, cancellationToken).ConfigureAwait(true);
                VisibleTasks.Add(childRow);
            }
        }

        SleepingTasks.Clear();
        var sleeping = await _taskService.ListSleepingRootsAsync(cancellationToken).ConfigureAwait(true);
        foreach (var item in sleeping.Where(t => t.FocusBucket == SelectedFocus))
        {
            SleepingTasks.Add(new TaskRowViewModel(item));
        }

        HasSleepingTasks = SleepingTasks.Count > 0;
    }

    private async Task ApplyTimeAsync(
        TaskRowViewModel row,
        Core.Entities.TimeSession? active,
        CancellationToken cancellationToken)
    {
        var totals = await _timeTracking.GetTotalsAsync(row.Id, cancellationToken).ConfigureAwait(true);
        var running = active is not null && active.TaskId.Equals(row.Id);
        var label = row.IsSubtask
            ? SessionDuration.FormatCompact(totals.Direct)
            : SessionDuration.FormatCompact(totals.RolledUp);
        row.ApplyTimer(running, label == "0m" && !running ? null : label);
    }

    [RelayCommand]
    private async Task ToggleTimerAsync(TaskRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        await _timeTracking.ToggleAsync(row.Id).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    partial void OnSelectedFocusChanged(FocusBucket value)
        => _ = RefreshAsync();

    [RelayCommand]
    private async Task QuickAddAsync()
    {
        if (string.IsNullOrWhiteSpace(QuickAddText))
        {
            return;
        }

        await _taskService.CreateAsync(new CreateTaskRequest
        {
            Title = QuickAddText,
            FocusBucket = FocusBucket.Inbox
        }).ConfigureAwait(true);

        QuickAddText = string.Empty;
        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ToggleExpandAsync(TaskRowViewModel? row)
    {
        if (row is null || row.IsSubtask)
        {
            return;
        }

        if (!_expanded.Add(row.Id))
        {
            _expanded.Remove(row.Id);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ToggleCompleteAsync(TaskRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        if (row.IsCompleted)
        {
            await _taskService.ReopenAsync(row.Id).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
            return;
        }

        var result = await _taskService.CompleteAsync(row.Id, null).ConfigureAwait(true);
        if (!result.Completed && result.IncompleteChildCount > 0)
        {
            var choice = ParentCompletionChoice.Cancel;
            if (ConfirmParentCompletion is not null)
            {
                choice = await ConfirmParentCompletion(
                    $"This task has {result.IncompleteChildCount} incomplete subtask(s). Complete parent only, complete all, or cancel?",
                    result.IncompleteChildCount).ConfigureAwait(true);
            }

            if (choice == ParentCompletionChoice.Cancel)
            {
                return;
            }

            await _taskService.CompleteAsync(row.Id, choice).ConfigureAwait(true);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task SoftDeleteAsync(TaskRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        await _taskService.SoftDeleteAsync(row.Id).ConfigureAwait(true);
        StatusMessage = "Task moved to recycle bin.";
        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task SleepAsync(TaskRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        await _taskService.SleepAsync(new SleepTaskRequest
        {
            Id = row.Id,
            Preset = SleepPreset.OneHour
        }).ConfigureAwait(true);
        StatusMessage = "Task sleeping for 1 hour.";
        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task WakeAsync(TaskRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        await _taskService.WakeAsync(row.Id).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void ShowNotes(TaskRowViewModel? row)
    {
        if (row is null || !row.HasNotes)
        {
            NotePopoverOpen = false;
            NotePreviewTask = null;
            return;
        }

        NotePreviewTask = row;
        NotePopoverOpen = true;
    }

    [RelayCommand]
    private void CloseNotes()
    {
        NotePopoverOpen = false;
        NotePreviewTask = null;
    }

    [RelayCommand]
    private async Task AddSubtaskAsync(TaskRowViewModel? row)
    {
        if (row is null || row.IsSubtask)
        {
            return;
        }

        await _taskService.CreateAsync(new CreateTaskRequest
        {
            Title = "New subtask",
            ParentTaskId = row.Id
        }).ConfigureAwait(true);

        _expanded.Add(row.Id);
        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void OpenManager() => OpenManagerRequested?.Invoke();

    [RelayCommand]
    private void Quit() => QuitRequested?.Invoke();

    [RelayCommand]
    private void ToggleSleepingList() => ShowSleeping = !ShowSleeping;

    public async Task PersistPlacementAsync(OverlayPlacement placement, CancellationToken cancellationToken = default)
    {
        placement.Opacity = Opacity;
        placement.Topmost = Topmost;
        Placement = placement;
        await _overlaySettings.SaveAsync(placement, cancellationToken).ConfigureAwait(true);
    }

    partial void OnOpacityChanged(double value)
    {
        Placement.Opacity = value;
        _ = _overlaySettings.SaveAsync(Placement);
    }

    partial void OnTopmostChanged(bool value)
    {
        Placement.Topmost = value;
        _ = _overlaySettings.SaveAsync(Placement);
    }
}
