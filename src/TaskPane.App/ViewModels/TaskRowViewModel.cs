using CommunityToolkit.Mvvm.ComponentModel;
using TaskPane.Application.Models;
using TaskPane.Core.Deadline;
using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using TaskPane.Core.Ids;
using TaskPane.Core.Sleep;

namespace TaskPane.App.ViewModels;

public partial class TaskRowViewModel : ObservableObject
{
    public TaskRowViewModel(
        TaskItem task,
        SubtaskProgress? progress = null,
        bool isExpanded = false,
        DateTimeOffset? utcNow = null,
        IReadOnlyList<string>? tagNames = null,
        string? projectName = null)
    {
        Id = task.Id;
        ParentTaskId = task.ParentTaskId;
        Apply(task, progress, isExpanded, utcNow, tagNames, projectName);
    }

    public TaskId Id { get; }
    public TaskId? ParentTaskId { get; }

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private bool _isCompleted;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _hasChildren;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private FocusBucket _focusBucket;

    [ObservableProperty]
    private TaskPriority _priority;

    [ObservableProperty]
    private bool _isSubtask;

    [ObservableProperty]
    private bool _isOverdue;

    [ObservableProperty]
    private bool _isSleeping;

    [ObservableProperty]
    private string _sleepWakeText = string.Empty;

    [ObservableProperty]
    private bool _isWaiting;

    [ObservableProperty]
    private bool _isBlocked;

    [ObservableProperty]
    private bool _hasDeletedPredecessorWarning;

    [ObservableProperty]
    private string _blockedText = string.Empty;

    [ObservableProperty]
    private string _deadlineText = string.Empty;

    [ObservableProperty]
    private string _waitingText = string.Empty;

    [ObservableProperty]
    private string _notesPreview = string.Empty;

    [ObservableProperty]
    private bool _hasNotes;

    [ObservableProperty]
    private string _tagsText = string.Empty;

    [ObservableProperty]
    private string _metaText = string.Empty;

    [ObservableProperty]
    private bool _isTimerRunning;

    [ObservableProperty]
    private string _timeText = string.Empty;

    [ObservableProperty]
    private string _timerButtonLabel = "▶";

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _draftTitle = string.Empty;

    public double ManualSortKey { get; private set; }

    public Guid? DeletionBatchId { get; private set; }

    public string? Notes { get; private set; }

    public string? DeadlineLocalDate { get; private set; }

    public string? DeadlineLocalTime { get; private set; }

    public string? WaitingFor { get; private set; }

    public DateTimeOffset? WaitingFollowUpUtc { get; private set; }

    public ProjectId? ProjectId { get; private set; }

    public string ProjectName { get; private set; } = string.Empty;

    public string? OccurrenceKey { get; private set; }

    public bool IsRecurring { get; private set; }

    public DateTimeOffset? SleepUntilUtc { get; private set; }

    public bool SleepIndefinitely { get; private set; }

    public void Apply(
        TaskItem task,
        SubtaskProgress? progress = null,
        bool? isExpanded = null,
        DateTimeOffset? utcNow = null,
        IReadOnlyList<string>? tagNames = null,
        string? projectName = null)
    {
        var now = utcNow ?? DateTimeOffset.UtcNow;
        Title = task.Title;
        IsCompleted = task.IsCompleted;
        FocusBucket = task.FocusBucket;
        Priority = task.Priority;
        IsSubtask = task.ParentTaskId is not null;
        ManualSortKey = task.ManualSortKey;
        DeletionBatchId = task.DeletionBatchId;
        Notes = task.Notes;
        HasNotes = !string.IsNullOrWhiteSpace(task.Notes);
        NotesPreview = BuildNotesPreview(task.Notes);
        DeadlineLocalDate = task.DeadlineLocalDate;
        DeadlineLocalTime = task.DeadlineLocalTime;
        WaitingFor = task.WaitingFor;
        WaitingFollowUpUtc = task.WaitingFollowUpUtc;
        ProjectId = task.ProjectId;
        ProjectName = projectName?.Trim() ?? string.Empty;
        OccurrenceKey = task.OccurrenceKey;
        IsRecurring = task.IsRecurringOccurrence;
        SleepUntilUtc = task.SleepUntilUtc;
        SleepIndefinitely = task.SleepIndefinitely;
        IsOverdue = DeadlineEvaluator.IsOverdue(task, now);
        IsSleeping = SleepEvaluator.IsCurrentlySleeping(task, now);
        SleepWakeText = SleepEvaluator.FormatWakeLabel(task, now);
        IsWaiting = task.IsWaiting;
        DeadlineText = DeadlineEvaluator.CompactPreview(task) ?? string.Empty;
        WaitingText = task.IsWaiting ? $"Waiting: {task.WaitingFor}" : string.Empty;
        TagsText = tagNames is { Count: > 0 } ? string.Join(", ", tagNames) : string.Empty;
        HasChildren = progress is { Total: > 0 };
        ProgressText = progress?.Display ?? string.Empty;
        MetaText = BuildMeta();
        if (isExpanded is { } expanded)
        {
            IsExpanded = expanded;
        }
    }

    public void ApplyBlocked(bool isBlocked, bool deletedPredecessorWarning, string? detail = null)
    {
        IsBlocked = isBlocked;
        HasDeletedPredecessorWarning = deletedPredecessorWarning;
        BlockedText = isBlocked
            ? (deletedPredecessorWarning ? "Blocked · missing predecessor" : "Blocked")
            : string.Empty;
        if (!string.IsNullOrWhiteSpace(detail) && isBlocked)
        {
            BlockedText = detail;
        }

        MetaText = BuildMeta();
    }

    public void ApplyTimer(bool isRunning, string? timeText)
    {
        IsTimerRunning = isRunning;
        TimeText = timeText ?? string.Empty;
        TimerButtonLabel = isRunning ? "■" : "▶";
        MetaText = BuildMeta();
    }

    /// <summary>Start overlay inline rename (select-all on focus happens in the window).</summary>
    public void BeginInlineEdit()
    {
        DraftTitle = Title;
        IsEditing = true;
    }

    public void CancelInlineEdit()
    {
        DraftTitle = Title;
        IsEditing = false;
    }

    public void ApplyCommittedTitle(string title)
    {
        Title = title;
        DraftTitle = title;
        IsEditing = false;
    }

    private string BuildMeta()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(DeadlineText))
        {
            parts.Add(IsOverdue ? $"Overdue · {DeadlineText}" : DeadlineText);
        }

        if (IsBlocked)
        {
            parts.Add(string.IsNullOrWhiteSpace(BlockedText) ? "Blocked" : BlockedText);
        }

        if (IsWaiting)
        {
            parts.Add(WaitingText);
        }

        if (IsSleeping)
        {
            parts.Add(string.IsNullOrWhiteSpace(SleepWakeText) ? "Sleeping" : $"Sleeping · {SleepWakeText}");
        }

        if (IsRecurring)
        {
            parts.Add(string.IsNullOrWhiteSpace(OccurrenceKey) ? "Recurring" : $"Recurring · {OccurrenceKey}");
        }

        if (!string.IsNullOrWhiteSpace(ProjectName))
        {
            parts.Add(ProjectName);
        }

        if (!string.IsNullOrWhiteSpace(TagsText))
        {
            parts.Add(TagsText);
        }

        if (!string.IsNullOrWhiteSpace(ProgressText))
        {
            parts.Add(ProgressText);
        }

        // Elapsed clock lives on the overlay active-task card only; keep row meta free of a second timer.
        if (!IsTimerRunning && !string.IsNullOrWhiteSpace(TimeText))
        {
            parts.Add(TimeText);
        }

        return string.Join(" · ", parts);
    }

    private static string BuildNotesPreview(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return string.Empty;
        }

        var flat = notes.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\n', ' ')
            .Trim();
        return flat.Length <= 80 ? flat : flat[..77] + "…";
    }
}
