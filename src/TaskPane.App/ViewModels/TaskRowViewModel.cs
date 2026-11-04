using CommunityToolkit.Mvvm.ComponentModel;
using TaskPane.Application.Models;
using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using TaskPane.Core.Ids;

namespace TaskPane.App.ViewModels;

public partial class TaskRowViewModel : ObservableObject
{
    public TaskRowViewModel(TaskItem task, SubtaskProgress? progress = null, bool isExpanded = false)
    {
        Id = task.Id;
        ParentTaskId = task.ParentTaskId;
        Apply(task, progress, isExpanded);
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

    public double ManualSortKey { get; private set; }

    public Guid? DeletionBatchId { get; private set; }

    public string? Notes { get; private set; }

    public void Apply(TaskItem task, SubtaskProgress? progress = null, bool? isExpanded = null)
    {
        Title = task.Title;
        IsCompleted = task.IsCompleted;
        FocusBucket = task.FocusBucket;
        Priority = task.Priority;
        IsSubtask = task.ParentTaskId is not null;
        ManualSortKey = task.ManualSortKey;
        DeletionBatchId = task.DeletionBatchId;
        Notes = task.Notes;
        HasChildren = progress is { Total: > 0 };
        ProgressText = progress?.Display ?? string.Empty;
        if (isExpanded is { } expanded)
        {
            IsExpanded = expanded;
        }
    }
}
