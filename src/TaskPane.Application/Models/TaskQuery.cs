using TaskPane.Core.Enums;
using TaskPane.Core.Ids;

namespace TaskPane.Application.Models;

public sealed class TaskQuery
{
    public FocusBucket? FocusBucket { get; init; }
    public string? SearchText { get; init; }
    public ProjectId? ProjectId { get; init; }
    public TagId? TagId { get; init; }
    public bool IncludeSleeping { get; init; }
    public bool OnlySleeping { get; init; }
    public bool OnlyWaiting { get; init; }
    public bool IncludeCompleted { get; init; }
    public TaskSortMode SortMode { get; init; } = TaskSortMode.Manual;

    /// <summary>When true, hide sleeping roots and children hidden by parent sleep (overlay mode).</summary>
    public bool OverlayVisibility { get; init; }
}
