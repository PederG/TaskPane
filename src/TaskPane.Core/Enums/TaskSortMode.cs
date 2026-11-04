namespace TaskPane.Core.Enums;

public enum TaskSortMode
{
    /// <summary>User-controlled ManualSortKey (default).</summary>
    Manual = 0,

    /// <summary>High → Normal → Low; undated tasks are not demoted.</summary>
    Priority = 1,

    /// <summary>Earliest deadline first; undated tasks sort after dated ones without changing priority.</summary>
    Deadline = 2,

    /// <summary>Newest created first.</summary>
    CreatedDesc = 3
}
