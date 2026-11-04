using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskPane.Application.Models;
using TaskPane.Application.Services;
using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using TaskPane.Core.Ids;
using TaskPane.Core.Recurrence;
using TaskPane.Core.Time;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using ReminderSettings = TaskPane.Core.Entities.ReminderSettings;
using AuditAction = TaskPane.Core.Enums.AuditAction;

namespace TaskPane.App.ViewModels;

public partial class ManagerViewModel : ObservableObject
{
    private readonly TaskService _taskService;
    private readonly OrganizationService _organization;
    private readonly HotkeySettingsService _hotkeySettings;
    private readonly TimeTrackingService _timeTracking;
    private readonly DependencyService _dependencies;
    private readonly HistoryService _history;
    private readonly RecurrenceService _recurrence;
    private readonly ReminderService _reminders;
    private readonly BackupService _backups;
    private readonly TransferService _transfer;
    private bool _suppressQueryRefresh;
    private bool _suppressNavSync;

    public ManagerViewModel(
        TaskService taskService,
        OrganizationService organization,
        HotkeySettingsService hotkeySettings,
        TimeTrackingService timeTracking,
        DependencyService dependencies,
        HistoryService history,
        RecurrenceService recurrence,
        ReminderService reminders,
        BackupService backups,
        TransferService transfer)
    {
        _taskService = taskService;
        _organization = organization;
        _hotkeySettings = hotkeySettings;
        _timeTracking = timeTracking;
        _dependencies = dependencies;
        _history = history;
        _recurrence = recurrence;
        _reminders = reminders;
        _backups = backups;
        _transfer = transfer;
        _selectedNavItem = NavItems[0];
        _workspaceTitle = NavItems[0].Title;
    }

    public ObservableCollection<TaskRowViewModel> Tasks { get; } = [];
    public ObservableCollection<TaskRowViewModel> DeletedTasks { get; } = [];
    public ObservableCollection<Project> Projects { get; } = [];
    public ObservableCollection<Tag> Tags { get; } = [];
    public ObservableCollection<Tag> SelectedTags { get; } = [];
    public ObservableCollection<SessionRowViewModel> Sessions { get; } = [];
    public ObservableCollection<string> ReportLines { get; } = [];
    public ObservableCollection<DependencyLinkView> DependsOnLinks { get; } = [];
    public ObservableCollection<DependencyLinkView> BlocksLinks { get; } = [];
    public ObservableCollection<HistoryRowViewModel> HistoryRows { get; } = [];
    public ObservableCollection<TaskRowViewModel> DependencyCandidates { get; } = [];

    public IReadOnlyList<ManagerNavItem> NavItems { get; } =
    [
        new(ManagerNavSection.Inbox, "Inbox", SymbolRegular.MailInbox24),
        new(ManagerNavSection.Today, "Today", SymbolRegular.CalendarToday24),
        new(ManagerNavSection.Next, "Next", SymbolRegular.ArrowNext24),
        new(ManagerNavSection.Someday, "Someday", SymbolRegular.WeatherMoon24),
        new(ManagerNavSection.Projects, "Projects", SymbolRegular.Folder24),
        new(ManagerNavSection.Sleeping, "Sleeping", SymbolRegular.Sleep24),
        new(ManagerNavSection.Completed, "Completed", SymbolRegular.CheckmarkCircle24),
        new(ManagerNavSection.RecycleBin, "Recycle Bin", SymbolRegular.BinRecycle24),
        new(ManagerNavSection.Reports, "Reports", SymbolRegular.DataHistogram24),
        new(ManagerNavSection.History, "History", SymbolRegular.History24)
    ];

    [ObservableProperty]
    private ManagerNavItem? _selectedNavItem;

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
    private bool _onlyBlocked;

    [ObservableProperty]
    private bool _hideBlocked;

    [ObservableProperty]
    private bool _showingRecycleBin;

    [ObservableProperty]
    private bool _showingHistory;

    [ObservableProperty]
    private bool _showingReports;

    [ObservableProperty]
    private bool _includeCompleted;

    [ObservableProperty]
    private bool _onlyCompleted;

    [ObservableProperty]
    private bool _isDarkTheme = true;

    [ObservableProperty]
    private string _workspaceTitle = "Inbox";

    [ObservableProperty]
    private TaskRowViewModel? _selectedPredecessor;

    [ObservableProperty]
    private string _blockedSummary = string.Empty;

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
    private int _idleThresholdMinutes = 10;

    [ObservableProperty]
    private string _editSessionStart = string.Empty;

    [ObservableProperty]
    private string _editSessionEnd = string.Empty;

    [ObservableProperty]
    private SessionRowViewModel? _selectedSession;

    [ObservableProperty]
    private string _timeTotalsText = string.Empty;

    [ObservableProperty]
    private string _reportSummary = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private RecurrenceFrequency _recurrenceFrequency = RecurrenceFrequency.Daily;

    [ObservableProperty]
    private int _recurrenceInterval = 1;

    [ObservableProperty]
    private string _recurrenceWeekdays = "1,2,3,4,5";

    [ObservableProperty]
    private int _recurrenceDayOfMonth = 1;

    [ObservableProperty]
    private string _recurrenceAnchorDate = string.Empty;

    [ObservableProperty]
    private string _recurrenceSummary = string.Empty;

    [ObservableProperty]
    private string _reminderDueLocal = string.Empty;

    [ObservableProperty]
    private string _reminderSummary = string.Empty;

    [ObservableProperty]
    private bool _quietHoursEnabled = true;

    [ObservableProperty]
    private string _quietStartLocal = "22:00";

    [ObservableProperty]
    private string _quietEndLocal = "07:00";

    [ObservableProperty]
    private string _backupStatusText = "Backup status unknown.";

    [ObservableProperty]
    private string _importPreviewText = string.Empty;

    [ObservableProperty]
    private ImportMode _selectedImportMode = ImportMode.MergeByIdSkipConflicts;

    public event Action? ReturnToOverlayRequested;
    public event Action? IdleThresholdChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await LoadCatalogAsync(cancellationToken).ConfigureAwait(true);
        var hotkey = await _hotkeySettings.GetAsync(cancellationToken).ConfigureAwait(true);
        HotkeyLabel = hotkey.DisplayLabel;
        HotkeyEnabled = hotkey.Enabled;
        var timeSettings = await _timeTracking.GetSettingsAsync(cancellationToken).ConfigureAwait(true);
        IdleThresholdMinutes = timeSettings.IdleThresholdMinutes;
        var reminderSettings = await _reminders.GetSettingsAsync(cancellationToken).ConfigureAwait(true);
        QuietHoursEnabled = reminderSettings.QuietHoursEnabled;
        QuietStartLocal = reminderSettings.QuietStartLocal;
        QuietEndLocal = reminderSettings.QuietEndLocal;
        await RefreshBackupStatusAsync(cancellationToken).ConfigureAwait(true);
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
        await RefreshReportAsync(cancellationToken).ConfigureAwait(true);
    }

    public async Task RefreshBackupStatusAsync(CancellationToken cancellationToken = default)
    {
        var status = await _backups.GetStatusAsync(cancellationToken).ConfigureAwait(true);
        BackupStatusText = status.StatusText;
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
        if (ShowingHistory)
        {
            await RefreshHistoryAsync(cancellationToken).ConfigureAwait(true);
            return;
        }

        if (ShowingRecycleBin)
        {
            DeletedTasks.Clear();
            var deleted = await _dependencies.ListRecycleBinEntriesAsync(cancellationToken).ConfigureAwait(true);
            foreach (var entry in deleted)
            {
                var row = new TaskRowViewModel(entry.Task);
                if (!string.IsNullOrWhiteSpace(entry.Warning))
                {
                    row.ApplyBlocked(false, entry.ParentMissingOrDeleted, entry.Warning);
                }

                DeletedTasks.Add(row);
            }

            return;
        }

        if (ShowingReports)
        {
            await RefreshReportAsync(cancellationToken).ConfigureAwait(true);
            return;
        }

        var query = BuildQuery();
        var active = await _timeTracking.GetActiveSessionAsync(cancellationToken).ConfigureAwait(true);
        Tasks.Clear();
        DependencyCandidates.Clear();
        var roots = await _taskService.QueryRootsAsync(query, cancellationToken).ConfigureAwait(true);
        var allVisibleIds = new List<TaskId>();
        foreach (var root in roots)
        {
            allVisibleIds.Add(root.Id);
            var childrenPreview = await _taskService.QueryChildrenAsync(root.Id, query, cancellationToken)
                .ConfigureAwait(true);
            allVisibleIds.AddRange(childrenPreview.Select(c => c.Id));
        }

        var blockedMap = await _dependencies.GetBlockedStatesAsync(allVisibleIds, cancellationToken)
            .ConfigureAwait(true);

        foreach (var root in roots)
        {
            var progress = await _taskService.GetProgressAsync(root.Id, cancellationToken).ConfigureAwait(true);
            var tagNames = await ResolveTagNamesAsync(root.Id, cancellationToken).ConfigureAwait(true);
            var row = new TaskRowViewModel(root, progress, isExpanded: true, tagNames: tagNames);
            await ApplyTimeAsync(row, active, cancellationToken).ConfigureAwait(true);
            ApplyBlocked(row, blockedMap);
            Tasks.Add(row);
            DependencyCandidates.Add(row);
            var children = await _taskService.QueryChildrenAsync(root.Id, query, cancellationToken).ConfigureAwait(true);
            foreach (var child in children)
            {
                var childTags = await ResolveTagNamesAsync(child.Id, cancellationToken).ConfigureAwait(true);
                var childRow = new TaskRowViewModel(child, tagNames: childTags);
                await ApplyTimeAsync(childRow, active, cancellationToken).ConfigureAwait(true);
                ApplyBlocked(childRow, blockedMap);
                Tasks.Add(childRow);
                DependencyCandidates.Add(childRow);
            }
        }
    }

    private static void ApplyBlocked(
        TaskRowViewModel row,
        IReadOnlyDictionary<TaskId, Core.Dependencies.BlockedState> blockedMap)
    {
        if (!blockedMap.TryGetValue(row.Id, out var state))
        {
            row.ApplyBlocked(false, false);
            return;
        }

        row.ApplyBlocked(state.IsBlocked, state.HasDeletedPredecessorWarning);
    }

    private async Task RefreshHistoryAsync(CancellationToken cancellationToken)
    {
        HistoryRows.Clear();
        var events = await _history.ListRecentAsync(80, cancellationToken).ConfigureAwait(true);
        foreach (var item in events)
        {
            HistoryRows.Add(new HistoryRowViewModel(item));
        }
    }

    private async Task ApplyTimeAsync(
        TaskRowViewModel row,
        TimeSession? active,
        CancellationToken cancellationToken)
    {
        var totals = await _timeTracking.GetTotalsAsync(row.Id, cancellationToken).ConfigureAwait(true);
        var running = active is not null && active.TaskId.Equals(row.Id);
        var label = row.IsSubtask
            ? SessionDuration.FormatCompact(totals.Direct)
            : SessionDuration.FormatCompact(totals.RolledUp);
        row.ApplyTimer(running, label == "0m" && !running ? null : label);
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
        IncludeCompleted = IncludeCompleted || OnlyCompleted,
        OnlyCompleted = OnlyCompleted,
        OnlyBlocked = OnlyBlocked,
        HideBlocked = HideBlocked && !OnlyBlocked,
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

    partial void OnSelectedNavItemChanged(ManagerNavItem? value)
    {
        if (value is null || _suppressNavSync)
        {
            return;
        }

        ApplyNavSection(value.Section);
    }

    partial void OnSelectedFocusChanged(FocusBucket? value)
    {
        if (_suppressQueryRefresh)
        {
            return;
        }

        ShowingRecycleBin = false;
        ShowingHistory = false;
        ShowingReports = false;
        _ = RefreshAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        if (!_suppressQueryRefresh)
        {
            _ = RefreshAsync();
        }
    }

    partial void OnFilterProjectChanged(Project? value)
    {
        if (!_suppressQueryRefresh)
        {
            _ = RefreshAsync();
        }
    }

    partial void OnFilterTagChanged(Tag? value)
    {
        if (!_suppressQueryRefresh)
        {
            _ = RefreshAsync();
        }
    }

    partial void OnIncludeSleepingChanged(bool value)
    {
        if (!_suppressQueryRefresh)
        {
            _ = RefreshAsync();
        }
    }

    partial void OnOnlySleepingChanged(bool value)
    {
        if (!_suppressQueryRefresh)
        {
            _ = RefreshAsync();
        }
    }

    partial void OnOnlyWaitingChanged(bool value)
    {
        if (!_suppressQueryRefresh)
        {
            _ = RefreshAsync();
        }
    }

    partial void OnOnlyBlockedChanged(bool value)
    {
        if (!_suppressQueryRefresh)
        {
            _ = RefreshAsync();
        }
    }

    partial void OnHideBlockedChanged(bool value)
    {
        if (!_suppressQueryRefresh)
        {
            _ = RefreshAsync();
        }
    }

    partial void OnSortModeChanged(TaskSortMode value)
    {
        if (!_suppressQueryRefresh)
        {
            _ = RefreshAsync();
        }
    }

    partial void OnShowingRecycleBinChanged(bool value)
    {
        if (_suppressQueryRefresh)
        {
            return;
        }

        if (value)
        {
            ShowingHistory = false;
            ShowingReports = false;
        }

        _ = RefreshAsync();
    }

    partial void OnShowingHistoryChanged(bool value)
    {
        if (_suppressQueryRefresh)
        {
            return;
        }

        if (value)
        {
            ShowingRecycleBin = false;
            ShowingReports = false;
        }

        _ = RefreshAsync();
    }

    partial void OnShowingReportsChanged(bool value)
    {
        if (_suppressQueryRefresh)
        {
            return;
        }

        if (value)
        {
            ShowingRecycleBin = false;
            ShowingHistory = false;
        }

        _ = RefreshAsync();
    }

    private void ApplyNavSection(ManagerNavSection section)
    {
        _suppressQueryRefresh = true;
        try
        {
            ShowingRecycleBin = false;
            ShowingHistory = false;
            ShowingReports = false;
            OnlySleeping = false;
            IncludeSleeping = false;
            IncludeCompleted = false;
            OnlyCompleted = false;
            WorkspaceTitle = SelectedNavItem?.Title ?? "Tasks";

            switch (section)
            {
                case ManagerNavSection.Inbox:
                    SelectedFocus = FocusBucket.Inbox;
                    break;
                case ManagerNavSection.Today:
                    SelectedFocus = FocusBucket.Today;
                    break;
                case ManagerNavSection.Next:
                    SelectedFocus = FocusBucket.Next;
                    break;
                case ManagerNavSection.Someday:
                    SelectedFocus = FocusBucket.Someday;
                    break;
                case ManagerNavSection.Projects:
                    SelectedFocus = null;
                    break;
                case ManagerNavSection.Sleeping:
                    SelectedFocus = null;
                    OnlySleeping = true;
                    IncludeSleeping = true;
                    break;
                case ManagerNavSection.Completed:
                    SelectedFocus = null;
                    OnlyCompleted = true;
                    IncludeCompleted = true;
                    break;
                case ManagerNavSection.RecycleBin:
                    ShowingRecycleBin = true;
                    break;
                case ManagerNavSection.Reports:
                    ShowingReports = true;
                    break;
                case ManagerNavSection.History:
                    ShowingHistory = true;
                    break;
            }
        }
        finally
        {
            _suppressQueryRefresh = false;
        }

        _ = RefreshAsync();
    }

    private void SyncNavSelection(ManagerNavSection section)
    {
        var match = NavItems.FirstOrDefault(i => i.Section == section);
        if (match is null || ReferenceEquals(SelectedNavItem, match))
        {
            return;
        }

        _suppressNavSync = true;
        try
        {
            SelectedNavItem = match;
            WorkspaceTitle = match.Title;
        }
        finally
        {
            _suppressNavSync = false;
        }
    }

    partial void OnSelectedTaskChanged(TaskRowViewModel? value)
    {
        SelectedTags.Clear();
        Sessions.Clear();
        DependsOnLinks.Clear();
        BlocksLinks.Clear();
        TimeTotalsText = string.Empty;
        BlockedSummary = string.Empty;
        SelectedSession = null;
        SelectedPredecessor = null;
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
        RecurrenceAnchorDate = string.IsNullOrWhiteSpace(RecurrenceAnchorDate)
            ? DateTime.Now.ToString("yyyy-MM-dd")
            : RecurrenceAnchorDate;
        ReminderDueLocal = DateTime.Now.AddHours(1).ToString("yyyy-MM-dd HH:mm");
        _ = LoadSelectedTagsAsync(value.Id);
        _ = LoadSessionsAsync(value.Id);
        _ = LoadDependenciesAsync(value.Id);
        _ = LoadRecurrenceAndRemindersAsync(value.Id);
    }

    private async Task LoadRecurrenceAndRemindersAsync(TaskId taskId)
    {
        var task = await _taskService.GetAsync(taskId).ConfigureAwait(true);
        if (task?.RecurrenceTemplateId is { } templateId)
        {
            var template = await _recurrence.GetTemplateAsync(templateId).ConfigureAwait(true);
            var rule = template is null ? null : _recurrence.ParseRule(template);
            RecurrenceSummary = rule is null
                ? $"Occurrence {task.OccurrenceKey}"
                : $"{rule.Frequency} every {rule.Interval} · key {task.OccurrenceKey}";
        }
        else
        {
            RecurrenceSummary = "No recurrence";
        }

        var reminders = await _reminders.ListForTaskAsync(taskId).ConfigureAwait(true);
        ReminderSummary = reminders.Count == 0
            ? "No reminders"
            : string.Join("; ", reminders.Select(r =>
            {
                var state = r.IsDismissed ? "dismissed" : r.IsDelivered ? "delivered" : "pending";
                return $"{r.DueUtc:u} ({state})";
            }));
    }

    private async Task LoadDependenciesAsync(TaskId taskId)
    {
        DependsOnLinks.Clear();
        BlocksLinks.Clear();
        var info = await _dependencies.GetTaskDependencyInfoAsync(taskId).ConfigureAwait(true);
        foreach (var link in info.DependsOn)
        {
            DependsOnLinks.Add(link);
        }

        foreach (var link in info.Blocks)
        {
            BlocksLinks.Add(link);
        }

        if (!info.Blocked.IsBlocked)
        {
            BlockedSummary = "Not blocked";
            return;
        }

        var parts = new List<string> { "Blocked" };
        if (info.Blocked.UnfinishedPredecessorIds.Count > 0)
        {
            parts.Add($"{info.Blocked.UnfinishedPredecessorIds.Count} unfinished predecessor(s)");
        }

        if (info.Blocked.HasDeletedPredecessorWarning)
        {
            parts.Add("deleted/missing predecessor warning");
        }

        BlockedSummary = string.Join(" · ", parts);
    }

    private async Task LoadSessionsAsync(TaskId taskId)
    {
        Sessions.Clear();
        var totals = await _timeTracking.GetTotalsAsync(taskId).ConfigureAwait(true);
        TimeTotalsText = $"Direct {SessionDuration.FormatCompact(totals.Direct)} · Roll-up {SessionDuration.FormatCompact(totals.RolledUp)}";
        var sessions = await _timeTracking.ListSessionsForTaskAsync(taskId).ConfigureAwait(true);
        foreach (var session in sessions.OrderByDescending(s => s.StartedUtc))
        {
            Sessions.Add(new SessionRowViewModel(session));
        }
    }

    partial void OnSelectedSessionChanged(SessionRowViewModel? value)
    {
        if (value is null)
        {
            EditSessionStart = string.Empty;
            EditSessionEnd = string.Empty;
            return;
        }

        EditSessionStart = value.StartedUtc.ToString("u");
        EditSessionEnd = value.EndedUtc?.ToString("u") ?? string.Empty;
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
    private void ShowRecycleBin()
    {
        SyncNavSelection(ManagerNavSection.RecycleBin);
        _suppressQueryRefresh = true;
        try
        {
            ShowingHistory = false;
            ShowingReports = false;
            ShowingRecycleBin = true;
        }
        finally
        {
            _suppressQueryRefresh = false;
        }

        WorkspaceTitle = "Recycle Bin";
        _ = RefreshAsync();
    }

    [RelayCommand]
    private void ShowHistory()
    {
        SyncNavSelection(ManagerNavSection.History);
        _suppressQueryRefresh = true;
        try
        {
            ShowingRecycleBin = false;
            ShowingReports = false;
            ShowingHistory = true;
        }
        finally
        {
            _suppressQueryRefresh = false;
        }

        WorkspaceTitle = "History";
        _ = RefreshAsync();
    }

    [RelayCommand]
    private void ShowTasks()
    {
        SyncNavSelection(ManagerNavSection.Inbox);
        ApplyNavSection(ManagerNavSection.Inbox);
    }

    [RelayCommand]
    private void ClearFocusFilter()
    {
        SyncNavSelection(ManagerNavSection.Projects);
        ApplyNavSection(ManagerNavSection.Projects);
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
        ApplicationThemeManager.Apply(
            IsDarkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light,
            WindowBackdropType.Mica,
            updateAccent: true);
    }

    [RelayCommand]
    private async Task AddDependencyAsync()
    {
        if (SelectedTask is null || SelectedPredecessor is null)
        {
            StatusMessage = "Select a predecessor task.";
            return;
        }

        try
        {
            await _dependencies.AddRequiredAsync(SelectedPredecessor.Id, SelectedTask.Id).ConfigureAwait(true);
            StatusMessage = "Required dependency added.";
            await LoadDependenciesAsync(SelectedTask.Id).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Core.Errors.BusinessException ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task RemoveDependencyAsync(DependencyLinkView? link)
    {
        if (link is null || SelectedTask is null)
        {
            return;
        }

        try
        {
            await _dependencies.RemoveAsync(link.EdgeId).ConfigureAwait(true);
            StatusMessage = "Dependency removed.";
            await LoadDependenciesAsync(SelectedTask.Id).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Core.Errors.BusinessException ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task UndoLatestAsync()
    {
        try
        {
            var undone = await _history.UndoLatestAsync().ConfigureAwait(true);
            StatusMessage = $"Undid {undone.Action}.";
            await RefreshAsync().ConfigureAwait(true);
            if (SelectedTask is not null)
            {
                await LoadDependenciesAsync(SelectedTask.Id).ConfigureAwait(true);
                await LoadSessionsAsync(SelectedTask.Id).ConfigureAwait(true);
            }
        }
        catch (Core.Errors.BusinessException ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task UndoHistoryRowAsync(HistoryRowViewModel? row)
    {
        if (row is null || row.IsUndone || !row.IsUndoable)
        {
            return;
        }

        try
        {
            await _history.UndoAsync(row.Id).ConfigureAwait(true);
            StatusMessage = $"Undid {row.Action}.";
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Core.Errors.BusinessException ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task ToggleTimerSelectedAsync()
    {
        if (SelectedTask is null)
        {
            return;
        }

        var blocked = await _dependencies.GetBlockedStateAsync(SelectedTask.Id).ConfigureAwait(true);
        await _timeTracking.ToggleAsync(SelectedTask.Id).ConfigureAwait(true);
        StatusMessage = blocked.IsBlocked
            ? "Timer started on blocked task (manual override)."
            : "Timer toggled.";
        await RefreshAsync().ConfigureAwait(true);
        await LoadSessionsAsync(SelectedTask.Id).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task SaveIdleThresholdAsync()
    {
        await _timeTracking.SaveIdleThresholdAsync(IdleThresholdMinutes).ConfigureAwait(true);
        StatusMessage = $"Idle threshold set to {IdleThresholdMinutes} minutes.";
        IdleThresholdChanged?.Invoke();
    }

    [RelayCommand]
    private async Task RefreshReportAsync(CancellationToken cancellationToken = default)
    {
        var today = await _timeTracking.GetTodayReportAsync(cancellationToken: cancellationToken).ConfigureAwait(true);
        var week = await _timeTracking.GetWeekReportAsync(cancellationToken: cancellationToken).ConfigureAwait(true);
        ReportSummary =
            $"{today.Label}: {SessionDuration.FormatCompact(today.Total)} · {week.Label}: {SessionDuration.FormatCompact(week.Total)}";
        ReportLines.Clear();
        ReportLines.Add($"— {today.Label} —");
        foreach (var row in today.Rows.Take(12))
        {
            ReportLines.Add($"{row.Title}: {SessionDuration.FormatCompact(row.Direct)}");
        }

        ReportLines.Add($"— {week.Label} —");
        foreach (var row in week.Rows.Take(12))
        {
            ReportLines.Add($"{row.Title}: {SessionDuration.FormatCompact(row.Direct)}");
        }
    }

    [RelayCommand]
    private async Task SaveSelectedSessionAsync()
    {
        if (SelectedSession is null || SelectedSession.IsOpen)
        {
            StatusMessage = "Select a closed session to edit.";
            return;
        }

        if (!DateTimeOffset.TryParse(EditSessionStart, out var start)
            || !DateTimeOffset.TryParse(EditSessionEnd, out var end))
        {
            StatusMessage = "Session start/end must be valid timestamps.";
            return;
        }

        await _timeTracking.AdjustSessionAsync(new SessionEditRequest
        {
            Id = SelectedSession.Id,
            StartedUtc = start.ToUniversalTime(),
            EndedUtc = end.ToUniversalTime(),
            IsDiscarded = SelectedSession.IsDiscarded
        }).ConfigureAwait(true);

        StatusMessage = "Session updated.";
        if (SelectedTask is not null)
        {
            await LoadSessionsAsync(SelectedTask.Id).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }

        await RefreshReportAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DiscardSelectedSessionAsync()
    {
        if (SelectedSession is null || SelectedSession.IsOpen)
        {
            StatusMessage = "Select a closed session to discard.";
            return;
        }

        await _timeTracking.DiscardSessionAsync(SelectedSession.Id).ConfigureAwait(true);
        StatusMessage = "Session discarded.";
        if (SelectedTask is not null)
        {
            await LoadSessionsAsync(SelectedTask.Id).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }

        await RefreshReportAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task AddManualSessionAsync()
    {
        if (SelectedTask is null)
        {
            return;
        }

        if (!DateTimeOffset.TryParse(EditSessionStart, out var start)
            || !DateTimeOffset.TryParse(EditSessionEnd, out var end))
        {
            var endUtc = DateTimeOffset.UtcNow;
            start = endUtc.AddMinutes(-30);
            end = endUtc;
            EditSessionStart = start.ToString("u");
            EditSessionEnd = end.ToString("u");
        }
        else
        {
            start = start.ToUniversalTime();
            end = end.ToUniversalTime();
        }

        await _timeTracking.AddManualSessionAsync(new ManualSessionRequest
        {
            TaskId = SelectedTask.Id,
            StartedUtc = start,
            EndedUtc = end
        }).ConfigureAwait(true);

        StatusMessage = "Manual session added.";
        await LoadSessionsAsync(SelectedTask.Id).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
        await RefreshReportAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task AttachRecurrenceAsync()
    {
        if (SelectedTask is null)
        {
            return;
        }

        var anchor = string.IsNullOrWhiteSpace(RecurrenceAnchorDate)
            ? DateTime.Now.ToString("yyyy-MM-dd")
            : RecurrenceAnchorDate.Trim();

        var rule = new RecurrenceRule
        {
            Frequency = RecurrenceFrequency,
            Interval = Math.Max(1, RecurrenceInterval),
            AnchorLocalDate = anchor,
            DayOfMonth = RecurrenceDayOfMonth,
            Weekdays = ParseWeekdays(RecurrenceWeekdays)
        };

        if (RecurrenceFrequency == RecurrenceFrequency.Monthly)
        {
            rule.MonthlyMode = MonthlyRecurrenceMode.DayOfMonth;
        }

        try
        {
            await _recurrence.AttachAsync(new AttachRecurrenceRequest
            {
                TaskId = SelectedTask.Id,
                Rule = rule,
                TimeZoneId = TimeZoneInfo.Local.Id,
                ConvertSourceToFirstOccurrence = true
            }).ConfigureAwait(true);
            StatusMessage = "Recurrence attached.";
            await RefreshAsync().ConfigureAwait(true);
            if (SelectedTask is not null)
            {
                await LoadRecurrenceAndRemindersAsync(SelectedTask.Id).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task DetachThisOccurrenceAsync()
    {
        if (SelectedTask is null)
        {
            return;
        }

        try
        {
            await _recurrence.EditOccurrenceAsync(new EditOccurrenceRequest
            {
                TaskId = SelectedTask.Id,
                Scope = OccurrenceEditScope.ThisOccurrenceOnly
            }).ConfigureAwait(true);
            StatusMessage = "Edited this occurrence only.";
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task ScheduleReminderAsync()
    {
        if (SelectedTask is null)
        {
            return;
        }

        if (!DateTime.TryParse(ReminderDueLocal, out var local))
        {
            StatusMessage = "Reminder due must be a local date/time.";
            return;
        }

        var due = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)).ToUniversalTime();
        try
        {
            await _reminders.ScheduleAsync(SelectedTask.Id, due).ConfigureAwait(true);
            StatusMessage = "Reminder scheduled.";
            await LoadRecurrenceAndRemindersAsync(SelectedTask.Id).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task SaveQuietHoursAsync()
    {
        try
        {
            await _reminders.SaveSettingsAsync(new ReminderSettings
            {
                QuietHoursEnabled = QuietHoursEnabled,
                QuietStartLocal = QuietStartLocal.Trim(),
                QuietEndLocal = QuietEndLocal.Trim()
            }).ConfigureAwait(true);
            StatusMessage = "Quiet hours saved.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task BackupNowAsync()
    {
        try
        {
            var result = await _backups.CreateBackupAsync("manual").ConfigureAwait(true);
            StatusMessage = result.Message;
            await RefreshBackupStatusAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task ExportJsonAsync()
    {
        try
        {
            var path = await _transfer.ExportJsonToFileAsync().ConfigureAwait(true);
            StatusMessage = $"JSON export saved: {path}";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task ExportMarkdownAsync()
    {
        try
        {
            var path = await _transfer.ExportMarkdownToFileAsync().ConfigureAwait(true);
            StatusMessage = $"Markdown export saved (not lossless): {path}";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task PreviewImportAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "TaskPane JSON (*.json)|*.json|All files (*.*)|*.*",
            Title = "Preview TaskPane JSON import"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var preview = await _transfer.PreviewImportFileAsync(dialog.FileName).ConfigureAwait(true);
            ImportPreviewText =
                $"Tasks {preview.TaskCount} (subtasks {preview.SubtaskCount}, deleted {preview.DeletedTaskCount}); " +
                $"deps {preview.DependencyCount}; sessions {preview.SessionCount}; " +
                $"projects {preview.ProjectCount}; tags {preview.TagCount}; " +
                $"recurrence {preview.RecurrenceTemplateCount}; reminders {preview.ReminderCount}. " +
                (preview.IsValid ? "Valid." : "INVALID: " + string.Join(" ", preview.Errors)) +
                (preview.Warnings.Count == 0 ? "" : " Warnings: " + string.Join(" ", preview.Warnings));
            StatusMessage = preview.IsValid ? "Import preview ready." : "Import preview found errors.";
        }
        catch (Exception ex)
        {
            ImportPreviewText = ex.Message;
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task ImportJsonAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "TaskPane JSON (*.json)|*.json|All files (*.*)|*.*",
            Title = "Import TaskPane JSON"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var preview = await _transfer.PreviewImportFileAsync(dialog.FileName).ConfigureAwait(true);
            if (!preview.IsValid)
            {
                StatusMessage = "Import blocked: " + string.Join(" ", preview.Errors);
                ImportPreviewText = StatusMessage;
                return;
            }

            var confirm = System.Windows.MessageBox.Show(
                $"Import {preview.TaskCount} tasks using mode {SelectedImportMode}?\n\n" +
                "A safety backup will be created first. Invalid imports cannot corrupt the database.",
                "Confirm import",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);
            if (confirm != System.Windows.MessageBoxResult.Yes)
            {
                return;
            }

            var result = await _transfer.ImportJsonFileAsync(
                dialog.FileName,
                SelectedImportMode,
                dryRun: false).ConfigureAwait(true);
            StatusMessage =
                $"Imported tasks {result.ImportedTasks} (skipped {result.SkippedTasks}). Safety backup: {result.SafetyBackupPath}";
            ImportPreviewText = StatusMessage;
            await LoadCatalogAsync(CancellationToken.None).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
            await RefreshBackupStatusAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task RestoreBackupAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "SQLite backup (*.db)|*.db|All files (*.*)|*.*",
            Title = "Restore TaskPane backup"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            "Restore this backup into the live database?\n\n" +
            "A safety backup of the current database is created first.\n" +
            _backups.GetRecoveryInstructions(),
            "Confirm restore",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _backups.RestoreFromBackupAsync(dialog.FileName).ConfigureAwait(true);
            StatusMessage = "Backup restored into live database.";
            await LoadCatalogAsync(CancellationToken.None).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
            await RefreshBackupStatusAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void ShowRecoveryHelp()
    {
        System.Windows.MessageBox.Show(
            _backups.GetRecoveryInstructions(),
            "Backup recovery",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }

    private static int[]? ParseWeekdays(string csv)
    {
        var parts = csv.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var days = new List<int>();
        foreach (var part in parts)
        {
            if (int.TryParse(part, out var d) && d is >= 0 and <= 6)
            {
                days.Add(d);
            }
        }

        return days.Count == 0 ? null : days.Distinct().ToArray();
    }

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
