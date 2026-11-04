using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskPane.Application.Models;
using TaskPane.Application.Services;
using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using TaskPane.Core.Ids;

namespace TaskPane.App.ViewModels;

public partial class ManagerViewModel : ObservableObject
{
    private readonly TaskService _taskService;
    private readonly OrganizationService _organization;
    private readonly HotkeySettingsService _hotkeySettings;

    public ManagerViewModel(
        TaskService taskService,
        OrganizationService organization,
        HotkeySettingsService hotkeySettings)
    {
        _taskService = taskService;
        _organization = organization;
        _hotkeySettings = hotkeySettings;
    }

    public ObservableCollection<TaskRowViewModel> Tasks { get; } = [];
    public ObservableCollection<TaskRowViewModel> DeletedTasks { get; } = [];
    public ObservableCollection<Project> Projects { get; } = [];
    public ObservableCollection<Tag> Tags { get; } = [];
    public ObservableCollection<Tag> SelectedTags { get; } = [];

    [ObservableProperty]
    private FocusBucket? _selectedFocus = FocusBucket.Inbox;

    [ObservableProperty]
    private TaskSortMode _sortMode = TaskSortMode.Manual;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private Project? _filterProject;

    [ObservableProperty]
    private Tag? _filterTag;

    [ObservableProperty]
    private bool _includeSleeping;

    [ObservableProperty]
    private bool _onlySleeping;

    [ObservableProperty]
    private bool _onlyWaiting;

    [ObservableProperty]
    private bool _showingRecycleBin;

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
    private string _editDeadlineDate = string.Empty;

    [ObservableProperty]
    private string _editDeadlineTime = string.Empty;

    [ObservableProperty]
    private string _editWaitingFor = string.Empty;

    [ObservableProperty]
    private string _editWaitingFollowUp = string.Empty;

    [ObservableProperty]
    private Project? _editProject;

    [ObservableProperty]
    private string _editTagsCsv = string.Empty;

    [ObservableProperty]
    private string _newTaskTitle = string.Empty;

    [ObservableProperty]
    private string _newProjectName = string.Empty;

    [ObservableProperty]
    private string _newTagName = string.Empty;

    [ObservableProperty]
    private string _hotkeyLabel = "Ctrl+Alt+Space";

    [ObservableProperty]
    private bool _hotkeyEnabled = true;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public event Action? ReturnToOverlayRequested;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await LoadCatalogAsync(cancellationToken).ConfigureAwait(true);
        var hotkey = await _hotkeySettings.GetAsync(cancellationToken).ConfigureAwait(true);
        HotkeyLabel = hotkey.DisplayLabel;
        HotkeyEnabled = hotkey.Enabled;
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    private async Task LoadCatalogAsync(CancellationToken cancellationToken)
    {
        Projects.Clear();
        foreach (var project in await _organization.ListProjectsAsync(cancellationToken: cancellationToken).ConfigureAwait(true))
        {
            Projects.Add(project);
        }

        Tags.Clear();
        foreach (var tag in await _organization.ListTagsAsync(cancellationToken).ConfigureAwait(true))
        {
            Tags.Add(tag);
        }
    }

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

        var query = BuildQuery();
        Tasks.Clear();
        var roots = await _taskService.QueryRootsAsync(query, cancellationToken).ConfigureAwait(true);
        foreach (var root in roots)
        {
            var progress = await _taskService.GetProgressAsync(root.Id, cancellationToken).ConfigureAwait(true);
            var tagNames = await ResolveTagNamesAsync(root.Id, cancellationToken).ConfigureAwait(true);
            Tasks.Add(new TaskRowViewModel(root, progress, isExpanded: true, tagNames: tagNames));
            var children = await _taskService.QueryChildrenAsync(root.Id, query, cancellationToken).ConfigureAwait(true);
            foreach (var child in children)
            {
                var childTags = await ResolveTagNamesAsync(child.Id, cancellationToken).ConfigureAwait(true);
                Tasks.Add(new TaskRowViewModel(child, tagNames: childTags));
            }
        }
    }

    private TaskQuery BuildQuery() => new()
    {
        FocusBucket = SelectedFocus,
        SearchText = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
        ProjectId = FilterProject?.Id,
        TagId = FilterTag?.Id,
        IncludeSleeping = IncludeSleeping || OnlySleeping,
        OnlySleeping = OnlySleeping,
        OnlyWaiting = OnlyWaiting,
        SortMode = SortMode,
        OverlayVisibility = false
    };

    private async Task<IReadOnlyList<string>> ResolveTagNamesAsync(TaskId taskId, CancellationToken cancellationToken)
    {
        var ids = await _organization.GetTaskTagIdsAsync(taskId, cancellationToken).ConfigureAwait(true);
        if (ids.Count == 0)
        {
            return [];
        }

        return Tags.Where(t => ids.Contains(t.Id)).Select(t => t.Name).ToList();
    }

    partial void OnSelectedFocusChanged(FocusBucket? value)
    {
        ShowingRecycleBin = false;
        _ = RefreshAsync();
    }

    partial void OnSearchTextChanged(string value) => _ = RefreshAsync();
    partial void OnFilterProjectChanged(Project? value) => _ = RefreshAsync();
    partial void OnFilterTagChanged(Tag? value) => _ = RefreshAsync();
    partial void OnIncludeSleepingChanged(bool value) => _ = RefreshAsync();
    partial void OnOnlySleepingChanged(bool value) => _ = RefreshAsync();
    partial void OnOnlyWaitingChanged(bool value) => _ = RefreshAsync();
    partial void OnSortModeChanged(TaskSortMode value) => _ = RefreshAsync();
    partial void OnShowingRecycleBinChanged(bool value) => _ = RefreshAsync();

    partial void OnSelectedTaskChanged(TaskRowViewModel? value)
    {
        SelectedTags.Clear();
        if (value is null)
        {
            EditTitle = string.Empty;
            EditNotes = string.Empty;
            EditDeadlineDate = string.Empty;
            EditDeadlineTime = string.Empty;
            EditWaitingFor = string.Empty;
            EditWaitingFollowUp = string.Empty;
            EditProject = null;
            EditTagsCsv = string.Empty;
            return;
        }

        EditTitle = value.Title;
        EditNotes = value.Notes ?? string.Empty;
        EditPriority = value.Priority;
        EditFocus = value.FocusBucket;
        EditDeadlineDate = value.DeadlineLocalDate ?? string.Empty;
        EditDeadlineTime = value.DeadlineLocalTime ?? string.Empty;
        EditWaitingFor = value.WaitingFor ?? string.Empty;
        EditWaitingFollowUp = value.WaitingFollowUpUtc?.ToString("u") ?? string.Empty;
        EditProject = Projects.FirstOrDefault(p => value.ProjectId is { } pid && p.Id.Equals(pid));
        EditTagsCsv = value.TagsText;
        _ = LoadSelectedTagsAsync(value.Id);
    }

    private async Task LoadSelectedTagsAsync(TaskId taskId)
    {
        SelectedTags.Clear();
        var ids = await _organization.GetTaskTagIdsAsync(taskId).ConfigureAwait(true);
        foreach (var tag in Tags.Where(t => ids.Contains(t.Id)))
        {
            SelectedTags.Add(tag);
        }
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
            FocusBucket = SelectedFocus ?? FocusBucket.Inbox
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

        DateTimeOffset? followUp = null;
        if (!string.IsNullOrWhiteSpace(EditWaitingFollowUp)
            && DateTimeOffset.TryParse(EditWaitingFollowUp, out var parsed))
        {
            followUp = parsed.ToUniversalTime();
        }

        var tagIds = await ResolveEditTagIdsAsync().ConfigureAwait(true);

        await _taskService.UpdateAsync(new UpdateTaskRequest
        {
            Id = SelectedTask.Id,
            Title = EditTitle,
            Notes = string.IsNullOrWhiteSpace(EditNotes) ? null : EditNotes,
            Priority = EditPriority,
            FocusBucket = EditFocus,
            DeadlineLocalDate = string.IsNullOrWhiteSpace(EditDeadlineDate) ? null : EditDeadlineDate,
            DeadlineLocalTime = string.IsNullOrWhiteSpace(EditDeadlineTime) ? null : EditDeadlineTime,
            DeadlineTimeZoneId = TimeZoneInfo.Local.Id,
            WaitingFor = string.IsNullOrWhiteSpace(EditWaitingFor) ? null : EditWaitingFor,
            WaitingFollowUpUtc = followUp,
            ProjectId = EditProject?.Id,
            TagIds = tagIds
        }).ConfigureAwait(true);

        StatusMessage = "Saved.";
        await LoadCatalogAsync(CancellationToken.None).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    private async Task<IReadOnlyList<TagId>> ResolveEditTagIdsAsync()
    {
        var names = EditTagsCsv
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var ids = new List<TagId>();
        foreach (var name in names)
        {
            var tag = await _organization.GetOrCreateTagAsync(name).ConfigureAwait(true);
            ids.Add(tag.Id);
        }

        return ids;
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
    private async Task SleepSelectedAsync(string? presetName)
    {
        if (SelectedTask is null)
        {
            return;
        }

        var preset = Enum.TryParse<SleepPreset>(presetName, ignoreCase: true, out var parsed)
            ? parsed
            : SleepPreset.OneHour;

        await _taskService.SleepAsync(new SleepTaskRequest
        {
            Id = SelectedTask.Id,
            Preset = preset
        }).ConfigureAwait(true);

        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task WakeSelectedAsync()
    {
        if (SelectedTask is null)
        {
            return;
        }

        await _taskService.WakeAsync(SelectedTask.Id).ConfigureAwait(true);
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
    private async Task MoveSelectedUpAsync()
    {
        if (SelectedTask is null)
        {
            return;
        }

        var peers = Tasks.Where(t => Nullable.Equals(t.ParentTaskId, SelectedTask.ParentTaskId)).ToList();
        var index = peers.FindIndex(t => t.Id.Equals(SelectedTask.Id));
        if (index <= 0)
        {
            return;
        }

        var above = peers[index - 1];
        await _taskService.ReorderBetweenAsync(SelectedTask.Id, beforeId: null, afterId: above.Id)
            .ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task MoveSelectedDownAsync()
    {
        if (SelectedTask is null)
        {
            return;
        }

        var peers = Tasks.Where(t => Nullable.Equals(t.ParentTaskId, SelectedTask.ParentTaskId)).ToList();
        var index = peers.FindIndex(t => t.Id.Equals(SelectedTask.Id));
        if (index < 0 || index >= peers.Count - 1)
        {
            return;
        }

        var below = peers[index + 1];
        await _taskService.ReorderBetweenAsync(SelectedTask.Id, beforeId: below.Id, afterId: null)
            .ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task PromoteSelectedAsync()
    {
        if (SelectedTask is null || !SelectedTask.IsSubtask)
        {
            return;
        }

        await _taskService.MoveAsync(SelectedTask.Id, newParentId: null).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task MakeSubtaskOfAsync(TaskRowViewModel? newParent)
    {
        if (SelectedTask is null || newParent is null || newParent.IsSubtask)
        {
            return;
        }

        if (SelectedTask.Id.Equals(newParent.Id))
        {
            return;
        }

        await _taskService.MoveAsync(SelectedTask.Id, newParent.Id).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task CreateProjectAsync()
    {
        if (string.IsNullOrWhiteSpace(NewProjectName))
        {
            return;
        }

        await _organization.CreateProjectAsync(NewProjectName).ConfigureAwait(true);
        NewProjectName = string.Empty;
        await LoadCatalogAsync(CancellationToken.None).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task CreateTagAsync()
    {
        if (string.IsNullOrWhiteSpace(NewTagName))
        {
            return;
        }

        await _organization.CreateTagAsync(NewTagName).ConfigureAwait(true);
        NewTagName = string.Empty;
        await LoadCatalogAsync(CancellationToken.None).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task SaveHotkeyAsync()
    {
        var setting = new HotkeySetting
        {
            DisplayLabel = string.IsNullOrWhiteSpace(HotkeyLabel) ? "Ctrl+Alt+Space" : HotkeyLabel.Trim(),
            Enabled = HotkeyEnabled,
            Modifiers = 0x0002 | 0x0001,
            VirtualKey = 0x20
        };

        // Parse simple Ctrl+Alt+Key labels for Space/letter keys.
        if (TryParseHotkey(setting.DisplayLabel, out var modifiers, out var vk))
        {
            setting.Modifiers = modifiers;
            setting.VirtualKey = vk;
        }

        await _hotkeySettings.SaveAsync(setting).ConfigureAwait(true);
        StatusMessage = "Hotkey saved. Restart registration on next overlay show.";
        HotkeyChanged?.Invoke(setting);
    }

    public event Action<HotkeySetting>? HotkeyChanged;

    private static bool TryParseHotkey(string label, out int modifiers, out int virtualKey)
    {
        modifiers = 0;
        virtualKey = 0x20;
        var parts = label.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        foreach (var part in parts.Take(parts.Length - 1))
        {
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)
                || part.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= 0x0002;
            }
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= 0x0001;
            }
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= 0x0004;
            }
            else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= 0x0008;
            }
        }

        var key = parts[^1];
        if (key.Equals("Space", StringComparison.OrdinalIgnoreCase))
        {
            virtualKey = 0x20;
            return modifiers != 0;
        }

        if (key.Length == 1)
        {
            var ch = char.ToUpperInvariant(key[0]);
            if (ch is >= 'A' and <= 'Z')
            {
                virtualKey = ch;
                return modifiers != 0;
            }

            if (ch is >= '0' and <= '9')
            {
                virtualKey = ch;
                return modifiers != 0;
            }
        }

        return false;
    }

    [RelayCommand]
    private void ReturnToOverlay() => ReturnToOverlayRequested?.Invoke();

    [RelayCommand]
    private void ShowRecycleBin() => ShowingRecycleBin = true;

    [RelayCommand]
    private void ShowTasks() => ShowingRecycleBin = false;

    [RelayCommand]
    private void ClearFocusFilter() => SelectedFocus = null;

    public async Task DropMoveToParentAsync(TaskRowViewModel source, TaskRowViewModel newParent)
    {
        if (newParent.IsSubtask || source.Id.Equals(newParent.Id))
        {
            return;
        }

        await _taskService.MoveAsync(source.Id, newParent.Id).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    public async Task DropReorderAsync(TaskRowViewModel source, TaskRowViewModel target)
    {
        if (!Nullable.Equals(source.ParentTaskId, target.ParentTaskId))
        {
            return;
        }

        if (source.ManualSortKey < target.ManualSortKey)
        {
            await _taskService.ReorderBetweenAsync(source.Id, beforeId: target.Id, afterId: null)
                .ConfigureAwait(true);
        }
        else
        {
            await _taskService.ReorderBetweenAsync(source.Id, beforeId: null, afterId: target.Id)
                .ConfigureAwait(true);
        }

        await RefreshAsync().ConfigureAwait(true);
    }
}
