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
        IReadOnlyList<string>? tagNames = null)
    {
        Id = task.Id;
        ParentTaskId = task.ParentTaskId;
        Apply(task, progress, isExpanded, utcNow, tagNames);
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
    private bool _isWaiting;

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

    public double ManualSortKey { get; private set; }

    public Guid? DeletionBatchId { get; private set; }

    public string? Notes { get; private set; }

    public string? DeadlineLocalDate { get; private set; }

    public string? DeadlineLocalTime { get; private set; }

    public string? WaitingFor { get; private set; }

    public DateTimeOffset? WaitingFollowUpUtc { get; private set; }

    public ProjectId? ProjectId { get; private set; }

    public void Apply(
        TaskItem task,
        SubtaskProgress? progress = null,
        bool? isExpanded = null,
        DateTimeOffset? utcNow = null,
        IReadOnlyList<string>? tagNames = null)
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
        IsOverdue = DeadlineEvaluator.IsOverdue(task, now);
        IsSleeping = SleepEvaluator.IsCurrentlySleeping(task, now);
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

    public void ApplyTimer(bool isRunning, string? timeText)
    {
        IsTimerRunning = isRunning;
        TimeText = timeText ?? string.Empty;
        TimerButtonLabel = isRunning ? "■" : "▶";
        MetaText = BuildMeta();
    }

    private string BuildMeta()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(DeadlineText))
        {
            parts.Add(IsOverdue ? $"Overdue · {DeadlineText}" : DeadlineText);
        }

        if (IsWaiting)
        {
            parts.Add(WaitingText);
        }

        if (IsSleeping)
        {
            parts.Add("Sleeping");
        }

        if (!string.IsNullOrWhiteSpace(TagsText))
        {
            parts.Add(TagsText);
        }

        if (!string.IsNullOrWhiteSpace(ProgressText))
        {
            parts.Add(ProgressText);
        }

        if (!string.IsNullOrWhiteSpace(TimeText))
        {
            parts.Add(TimeText);
        }

        if (IsTimerRunning)
        {
            parts.Add("Tracking");
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
