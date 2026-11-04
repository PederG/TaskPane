namespace TaskPane.Core.Ids;

/// <summary>Stable UUID identifier for a project.</summary>
public readonly record struct ProjectId(Guid Value) : IComparable<ProjectId>
{
    public static ProjectId New() => new(Guid.NewGuid());

    public static ProjectId Parse(string value) => new(Guid.Parse(value));

    public static bool TryParse(string? value, out ProjectId id)
    {
        if (Guid.TryParse(value, out var guid))
        {
            id = new ProjectId(guid);
            return true;
        }

        id = default;
        return false;
    }

    public int CompareTo(ProjectId other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString("D");
}
