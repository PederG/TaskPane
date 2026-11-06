using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskPane.Application.Models;
using TaskPane.Application.Services;
using TaskPane.Core.Branding;
using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using TaskPane.Core.Ids;
using TaskPane.Core.Notes;
using TaskPane.Core.Overlay;
using TaskPane.Core.Time;
using TaskPane.Core.Validation;

namespace TaskPane.App.ViewModels;

public partial class OverlayViewModel : ObservableObject
{
    private readonly TaskService _taskService;
    private readonly OverlaySettingsService _overlaySettings;
    private readonly AppSettingsService _appSettings;
    private readonly TimeTrackingService _timeTracking;
    private readonly DependencyService _dependencies;
    private readonly NotesDraftCache _noteDrafts = new();
    private readonly HashSet<TaskId> _expanded = [];
    private DateTimeOffset? _activeStartedUtc;
    private TaskId? _activeTaskId;
    private TaskId? _noteTaskId;

    public OverlayViewModel(
        TaskService taskService,
        OverlaySettingsService overlaySettings,
        AppSettingsService appSettings,
        TimeTrackingService timeTracking,
        DependencyService dependencies)
    {
        _taskService = taskService;
        _overlaySettings = overlaySettings;
        _appSettings = appSettings;
        _timeTracking = timeTracking;
        _dependencies = dependencies;
    }

    public ObservableCollection<TaskRowViewModel> VisibleTasks { get; } = [];
    public ObservableCollection<TaskRowViewModel> SleepingTasks { get; } = [];

    [ObservableProperty]
    private FocusBucket _selectedFocus = FocusBucket.Inbox;

    [ObservableProperty]
    private string _quickAddText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _topmost = true;

    [ObservableProperty]
    private OverlayPlacement _placement = new();

    [ObservableProperty]
    private double _maxContentHeight = 720;

    [ObservableProperty]
    private TaskRowViewModel? _notePreviewTask;

    [ObservableProperty]
    private bool _notePopoverOpen;

    [ObservableProperty]
    private bool _isNoteEditing;

    [ObservableProperty]
    private string _noteDraftText = string.Empty;

    [ObservableProperty]
    private string _noteCompactBody = string.Empty;

    [ObservableProperty]
    private bool _showSleeping;

    [ObservableProperty]
    private bool _hasSleepingTasks;

    [ObservableProperty]
    private bool _hideBlocked;

    [ObservableProperty]
    private string _displayName = AppBranding.DefaultAppDisplayName;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _searchOpen;

    [ObservableProperty]
    private int _visibleTaskCount;

    [ObservableProperty]
    private int _activeTaskCount;

    [ObservableProperty]
    private int _completedCount;

    [ObservableProperty]
    private int _sleepingCount;

    [ObservableProperty]
    private string _focusLabel = "Inbox";

    [ObservableProperty]
    private string _taskCountLabel = "0 tasks";

    [ObservableProperty]
    private string _activeCountLabel = "0 active";

    [ObservableProperty]
    private string _footerCountsLabel = "0 sleeping · 0 completed";

    [ObservableProperty]
    private bool _hasActiveTask;

    [ObservableProperty]
    private string _activeTaskTitle = string.Empty;

    [ObservableProperty]
    private string _activeElapsedText = string.Empty;

    [ObservableProperty]
    private TaskRowViewModel? _activeTaskRow;

    /// <summary>
    /// True while the overlay HWND is activated / keyboard-focused for chrome policy.
    /// Starts true so the first show is full chrome until the user leaves the window.
    /// </summary>
    [ObservableProperty]
    private bool _isOverlayFocused = true;

    /// <summary>
    /// Mini always-on-top strip (title + elapsed + pause) while a timer runs and the overlay
    /// is not focused. Not tray minimize — window stays visible and Topmost.
    /// </summary>
    [ObservableProperty]
    private bool _isMiniSessionChrome;

    /// <summary>List viewport ceiling so SizeToContent can measure without unbounded growth.</summary>
    public double TaskListMaxHeight => Math.Max(120, MaxContentHeight - 280);

    /// <summary>Window MinHeight for the current chrome mode (full vs running-session mini).</summary>
    public double OverlayMinHeight => OverlaySessionChrome.MinHeightDip(IsMiniSessionChrome);

    public event Action? OpenManagerRequested;
    public event Action? QuitRequested;
    public Func<string, int, Task<ParentCompletionChoice>>? ConfirmParentCompletion { get; set; }

    /// <summary>Shows a custom sleep-until picker; return null to cancel.</summary>
    public Func<TaskRowViewModel, DateTimeOffset?>? PromptCustomSleepUntil { get; set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Placement = await _overlaySettings.GetAsync(cancellationToken).ConfigureAwait(true);
        Topmost = Placement.Topmost;
        MaxContentHeight = Placement.MaxHeight > 0 ? Placement.MaxHeight : 720;

        var app = await _appSettings.GetAsync(cancellationToken).ConfigureAwait(true);
        DisplayName = string.IsNullOrWhiteSpace(app.AppDisplayName)
            ? AppBranding.DefaultAppDisplayName
            : app.AppDisplayName;

        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var active = await _timeTracking.GetActiveSessionAsync(cancellationToken).ConfigureAwait(true);
        var query = new TaskQuery
        {
            FocusBucket = SelectedFocus,
            OverlayVisibility = true,
            HideBlocked = HideBlocked,
            SearchText = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim()
        };
        var roots = await _taskService.QueryRootsAsync(query, cancellationToken).ConfigureAwait(true);
        VisibleTasks.Clear();

        var ids = new List<TaskId>();
        foreach (var root in roots)
        {
            ids.Add(root.Id);
        }

        var blockedMap = await _dependencies.GetBlockedStatesAsync(ids, cancellationToken).ConfigureAwait(true);
        TaskRowViewModel? activeRow = null;

        foreach (var root in roots)
        {
            var progress = await _taskService.GetProgressAsync(root.Id, cancellationToken).ConfigureAwait(true);
            var expanded = _expanded.Contains(root.Id);
            var row = new TaskRowViewModel(root, progress, expanded);
            await ApplyTimeAsync(row, active, cancellationToken).ConfigureAwait(true);
            if (blockedMap.TryGetValue(root.Id, out var state))
            {
                row.ApplyBlocked(state.IsBlocked, state.HasDeletedPredecessorWarning);
            }

            VisibleTasks.Add(row);
            if (row.IsTimerRunning)
            {
                activeRow = row;
            }

            if (!expanded)
            {
                continue;
            }

            var children = await _taskService.QueryChildrenAsync(
                root.Id,
                new TaskQuery
                {
                    OverlayVisibility = true,
                    FocusBucket = SelectedFocus,
                    HideBlocked = HideBlocked,
                    SearchText = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim()
                },
                cancellationToken).ConfigureAwait(true);
            var childBlocked = await _dependencies
                .GetBlockedStatesAsync(children.Select(c => c.Id).ToList(), cancellationToken)
                .ConfigureAwait(true);
            foreach (var child in children)
            {
                var childRow = new TaskRowViewModel(child);
                await ApplyTimeAsync(childRow, active, cancellationToken).ConfigureAwait(true);
                if (childBlocked.TryGetValue(child.Id, out var childState))
                {
                    childRow.ApplyBlocked(childState.IsBlocked, childState.HasDeletedPredecessorWarning);
                }

                VisibleTasks.Add(childRow);
                if (childRow.IsTimerRunning)
                {
                    activeRow = childRow;
                }
            }
        }

        SleepingTasks.Clear();
        var sleeping = await _taskService.ListSleepingRootsAsync(cancellationToken).ConfigureAwait(true);
        foreach (var item in sleeping.Where(t => t.FocusBucket == SelectedFocus))
        {
            SleepingTasks.Add(new TaskRowViewModel(item));
        }

        HasSleepingTasks = SleepingTasks.Count > 0;
        SleepingCount = SleepingTasks.Count;

        var completed = await _taskService.QueryRootsAsync(
            new TaskQuery
            {
                FocusBucket = SelectedFocus,
                OnlyCompleted = true
            },
            cancellationToken).ConfigureAwait(true);
        CompletedCount = completed.Count;

        VisibleTaskCount = VisibleTasks.Count(t => !t.IsSubtask);
        FocusLabel = SelectedFocus.ToString();
        TaskCountLabel = VisibleTaskCount == 1 ? "1 task" : $"{VisibleTaskCount} tasks";
        FooterCountsLabel = $"{SleepingCount} sleeping · {CompletedCount} completed";

        if (active is not null)
        {
            _activeTaskId = active.TaskId;
            _activeStartedUtc = active.StartedUtc;
            HasActiveTask = true;
            ActiveTaskCount = 1;
            ActiveCountLabel = "1 active";
            ActiveTaskRow = activeRow;
            ActiveTaskTitle = activeRow?.Title
                ?? (await _taskService.GetAsync(active.TaskId, cancellationToken).ConfigureAwait(true))?.Title
                ?? "Active task";
            TickActiveElapsed();
        }
        else
        {
            _activeTaskId = null;
            _activeStartedUtc = null;
            HasActiveTask = false;
            ActiveTaskCount = 0;
            ActiveCountLabel = "0 active";
            ActiveTaskRow = null;
            ActiveTaskTitle = string.Empty;
            ActiveElapsedText = string.Empty;
        }

        UpdateSessionChromeMode();
        RebindOpenNotesTarget();
    }

    /// <summary>Overlay Activated / GotFocus — expand to full compact chrome.</summary>
    public void NotifyOverlayActivated()
    {
        IsOverlayFocused = true;
        UpdateSessionChromeMode();
    }

    /// <summary>Overlay Deactivated / LostFocus — collapse to mini while a timer runs.</summary>
    public void NotifyOverlayDeactivated()
    {
        IsOverlayFocused = false;
        UpdateSessionChromeMode();
    }

    /// <summary>Click on the mini strip expands full chrome (window should also activate).</summary>
    [RelayCommand]
    private void ExpandSessionChrome()
    {
        IsOverlayFocused = true;
        UpdateSessionChromeMode();
    }

    private void UpdateSessionChromeMode()
    {
        IsMiniSessionChrome = OverlaySessionChrome.ShouldShowMini(HasActiveTask, IsOverlayFocused);
    }

    partial void OnIsMiniSessionChromeChanged(bool value)
    {
        OnPropertyChanged(nameof(OverlayMinHeight));
        if (!value)
        {
            return;
        }

        // Mini strip is title + elapsed + pause only — fold secondary panels.
        if (NotePopoverOpen)
        {
            CloseNotes();
        }

        SearchOpen = false;
        ShowSleeping = false;
    }

    /// <summary>Updates the active-card elapsed clock without a full list refresh.</summary>
    public void TickActiveElapsed()
    {
        if (_activeStartedUtc is null)
        {
            ActiveElapsedText = string.Empty;
            return;
        }

        var elapsed = DateTimeOffset.UtcNow - _activeStartedUtc.Value;
        ActiveElapsedText = $"{SessionDuration.FormatClock(elapsed)} elapsed";
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

        var blocked = await _dependencies.GetBlockedStateAsync(row.Id).ConfigureAwait(true);
        await _timeTracking.ToggleAsync(row.Id).ConfigureAwait(true);
        if (blocked.IsBlocked)
        {
            StatusMessage = "Timer override on blocked task.";
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task PauseActiveAsync()
    {
        if (_activeTaskId is null)
        {
            return;
        }

        await _timeTracking.StopAsync().ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    partial void OnSelectedFocusChanged(FocusBucket value)
        => _ = RefreshAsync();

    partial void OnHideBlockedChanged(bool value)
        => _ = RefreshAsync();

    partial void OnSearchTextChanged(string value)
        => _ = RefreshAsync();

    [RelayCommand]
    private void SelectFocus(FocusBucket bucket) => SelectedFocus = bucket;

    [RelayCommand]
    private void ToggleSearch()
    {
        SearchOpen = !SearchOpen;
        if (!SearchOpen)
        {
            SearchText = string.Empty;
        }
    }

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
        => await SleepWithPresetAsync(row, SleepPreset.OneHour).ConfigureAwait(true);

    [RelayCommand]
    private async Task SleepWithPresetAsync(SleepPresetArgs? args)
    {
        if (args is null)
        {
            return;
        }

        await SleepWithPresetAsync(args.Row, args.Preset).ConfigureAwait(true);
    }

    public async Task SleepWithPresetAsync(TaskRowViewModel? row, SleepPreset preset)
    {
        if (row is null)
        {
            return;
        }

        DateTimeOffset? customUntil = null;
        if (preset == SleepPreset.Custom)
        {
            customUntil = PromptCustomSleepUntil?.Invoke(row);
            if (customUntil is null)
            {
                return;
            }
        }

        await _taskService.SleepAsync(new SleepTaskRequest
        {
            Id = row.Id,
            Preset = preset,
            CustomUntilUtc = customUntil
        }).ConfigureAwait(true);
        StatusMessage = SleepStatusMessage(preset);
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
        StatusMessage = "Task woken.";
        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Overlay auto-wake: when a timed sleep has expired, refresh so the task reappears
    /// without an explicit Wake (deadline/notes/priority/deps/time remain untouched).
    /// </summary>
    public async Task CheckAutoWakeAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var expiredVisible = SleepingTasks.Any(t =>
            !t.SleepIndefinitely
            && t.SleepUntilUtc is { } until
            && until <= now);
        if (!expiredVisible)
        {
            return;
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    private static string SleepStatusMessage(SleepPreset preset) => preset switch
    {
        SleepPreset.OneHour => "Task sleeping for 1 hour.",
        SleepPreset.TomorrowMorning => "Task sleeping until tomorrow morning.",
        SleepPreset.NextWeek => "Task sleeping until next week.",
        SleepPreset.Indefinitely => "Task sleeping indefinitely.",
        SleepPreset.Custom => "Task sleeping until the chosen time.",
        _ => "Task sleeping."
    };

    public sealed record SleepPresetArgs(TaskRowViewModel Row, SleepPreset Preset);

    [RelayCommand]
    private void ShowNotes(TaskRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        StashCurrentNoteDraft();
        OpenNotesFor(row, beginEditing: !row.HasNotes);
    }

    [RelayCommand]
    private void BeginEditNotes()
    {
        if (!NotePopoverOpen || NotePreviewTask is null || _noteTaskId is null)
        {
            return;
        }

        IsNoteEditing = true;
        NoteDraftText = _noteDrafts.OpenOrRestore(_noteTaskId.Value, NotePreviewTask.Notes);
        _noteDrafts.SetDraft(_noteTaskId.Value, NoteDraftText);
    }

    [RelayCommand]
    private async Task SaveNotesAsync()
    {
        if (NotePreviewTask is null || _noteTaskId is null)
        {
            return;
        }

        var row = NotePreviewTask;
        var notes = NotesDraftCache.NormalizeForPersist(NoteDraftText);
        await _taskService.UpdateAsync(new UpdateTaskRequest
        {
            Id = row.Id,
            Title = row.Title,
            Notes = notes,
            Priority = row.Priority,
            FocusBucket = row.FocusBucket,
            DeadlineLocalDate = row.DeadlineLocalDate,
            DeadlineLocalTime = row.DeadlineLocalTime,
            WaitingFor = row.WaitingFor,
            WaitingFollowUpUtc = row.WaitingFollowUpUtc,
            ProjectId = row.ProjectId
        }).ConfigureAwait(true);

        _noteDrafts.Clear(row.Id);
        IsNoteEditing = false;
        StatusMessage = notes is null ? "Notes cleared." : "Notes saved.";
        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void CancelNotesEdit()
    {
        if (_noteTaskId is null)
        {
            IsNoteEditing = false;
            return;
        }

        _noteDrafts.Clear(_noteTaskId.Value);
        NoteDraftText = NotePreviewTask?.Notes ?? string.Empty;
        IsNoteEditing = false;
        UpdateNoteCompactBody(NotePreviewTask?.Notes);
    }

    [RelayCommand]
    private void CloseNotes()
    {
        StashCurrentNoteDraft();
        NotePopoverOpen = false;
        IsNoteEditing = false;
        NotePreviewTask = null;
        _noteTaskId = null;
        NoteDraftText = string.Empty;
        NoteCompactBody = string.Empty;
    }

    partial void OnNoteDraftTextChanged(string value)
    {
        if (_noteTaskId is null || !IsNoteEditing)
        {
            return;
        }

        _noteDrafts.SetDraft(_noteTaskId.Value, value);
    }

    private void OpenNotesFor(TaskRowViewModel row, bool beginEditing)
    {
        _noteTaskId = row.Id;
        NotePreviewTask = row;
        NoteDraftText = _noteDrafts.OpenOrRestore(row.Id, row.Notes);
        UpdateNoteCompactBody(
            _noteDrafts.TryGetDraft(row.Id, out var draft) ? draft : row.Notes);
        IsNoteEditing = beginEditing || _noteDrafts.IsDirty(row.Id, row.Notes);
        if (IsNoteEditing)
        {
            _noteDrafts.SetDraft(row.Id, NoteDraftText);
        }

        NotePopoverOpen = true;
    }

    private void StashCurrentNoteDraft()
    {
        if (_noteTaskId is null || !IsNoteEditing)
        {
            return;
        }

        _noteDrafts.SetDraft(_noteTaskId.Value, NoteDraftText);
    }

    private void RebindOpenNotesTarget()
    {
        if (!NotePopoverOpen || _noteTaskId is null)
        {
            return;
        }

        var id = _noteTaskId.Value;
        var row = VisibleTasks.FirstOrDefault(t => t.Id.Equals(id))
            ?? SleepingTasks.FirstOrDefault(t => t.Id.Equals(id));
        if (row is null)
        {
            // Task left the overlay (completed/deleted/focus change) — keep draft, close UI.
            StashCurrentNoteDraft();
            NotePopoverOpen = false;
            IsNoteEditing = false;
            NotePreviewTask = null;
            _noteTaskId = null;
            return;
        }

        var wasEditing = IsNoteEditing;
        NotePreviewTask = row;
        if (wasEditing)
        {
            NoteDraftText = _noteDrafts.OpenOrRestore(id, row.Notes);
        }
        else
        {
            NoteDraftText = row.Notes ?? string.Empty;
            UpdateNoteCompactBody(
                _noteDrafts.TryGetDraft(id, out var draft) ? draft : row.Notes);
        }
    }

    private void UpdateNoteCompactBody(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            NoteCompactBody = "No notes yet — Edit to add Markdown or plain text.";
            return;
        }

        NoteCompactBody = notes;
    }

    private static bool CanAddSubtask(TaskRowViewModel? row)
        => row is not null && SubtaskUiPolicy.CanOfferAddSubtask(row.IsSubtask);

    [RelayCommand(CanExecute = nameof(CanAddSubtask))]
    private async Task AddSubtaskAsync(TaskRowViewModel? row)
    {
        if (!CanAddSubtask(row))
        {
            return;
        }

        var created = await _taskService.CreateAsync(new CreateTaskRequest
        {
            Title = SubtaskUiPolicy.NewSubtaskPlaceholderTitle,
            ParentTaskId = row!.Id
        }).ConfigureAwait(true);

        _expanded.Add(row.Id);
        await RefreshAsync().ConfigureAwait(true);

        var child = VisibleTasks.FirstOrDefault(t => t.Id.Equals(created.Id));
        child?.BeginInlineEdit();
    }

    [RelayCommand]
    private async Task CommitInlineEditAsync(TaskRowViewModel? row)
    {
        if (row is null || !row.IsEditing)
        {
            return;
        }

        var title = row.DraftTitle.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            row.CancelInlineEdit();
            return;
        }

        if (!string.Equals(title, row.Title, StringComparison.Ordinal))
        {
            await _taskService.UpdateAsync(new UpdateTaskRequest
            {
                Id = row.Id,
                Title = title,
                Notes = row.Notes,
                Priority = row.Priority,
                FocusBucket = row.FocusBucket,
                DeadlineLocalDate = row.DeadlineLocalDate,
                DeadlineLocalTime = row.DeadlineLocalTime,
                WaitingFor = row.WaitingFor,
                WaitingFollowUpUtc = row.WaitingFollowUpUtc,
                ProjectId = row.ProjectId
            }).ConfigureAwait(true);
        }

        row.ApplyCommittedTitle(title);
        if (ActiveTaskRow?.Id.Equals(row.Id) == true)
        {
            ActiveTaskTitle = title;
        }
    }

    [RelayCommand]
    private void CancelInlineEdit(TaskRowViewModel? row) => row?.CancelInlineEdit();

    [RelayCommand]
    private void OpenManager() => OpenManagerRequested?.Invoke();

    [RelayCommand]
    private void Quit() => QuitRequested?.Invoke();

    [RelayCommand]
    private void ToggleSleepingList() => ShowSleeping = !ShowSleeping;

    public async Task PersistPlacementAsync(OverlayPlacement placement, CancellationToken cancellationToken = default)
    {
        placement.Topmost = Topmost;
        placement.MaxHeight = MaxContentHeight;
        Placement = placement;
        await _overlaySettings.SaveAsync(placement, cancellationToken).ConfigureAwait(true);
    }

    partial void OnTopmostChanged(bool value)
    {
        Placement.Topmost = value;
        _ = _overlaySettings.SaveAsync(Placement);
    }

    partial void OnMaxContentHeightChanged(double value)
    {
        Placement.MaxHeight = value;
        OnPropertyChanged(nameof(TaskListMaxHeight));
        _ = _overlaySettings.SaveAsync(Placement);
    }
}
