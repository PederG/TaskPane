namespace TaskPane.Core.Ids;

/// <summary>Stable UUID identifier for a task.</summary>
public readonly record struct TaskId(Guid Value) : IComparable<TaskId>
{
    public static TaskId New() => new(Guid.NewGuid());

    public static TaskId Parse(string value) => new(Guid.Parse(value));

    public static bool TryParse(string? value, out TaskId id)
    {
        if (Guid.TryParse(value, out var guid))
        {
            id = new TaskId(guid);
            return true;
        }

        id = default;
        return false;
    }

    public int CompareTo(TaskId other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString("D");
}
