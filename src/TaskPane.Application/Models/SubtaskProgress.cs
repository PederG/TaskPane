using TaskPane.Core.Ids;

namespace TaskPane.Application.Models;

public sealed record SubtaskProgress(TaskId ParentId, int Completed, int Total)
{
    public string Display => Total == 0 ? string.Empty : $"{Completed}/{Total}";
}
