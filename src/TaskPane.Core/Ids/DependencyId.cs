namespace TaskPane.Core.Ids;

/// <summary>Stable UUID identifier for a dependency edge.</summary>
public readonly record struct DependencyId(Guid Value) : IComparable<DependencyId>
{
    public static DependencyId New() => new(Guid.NewGuid());

    public static DependencyId Parse(string value) => new(Guid.Parse(value));

    public static bool TryParse(string? value, out DependencyId id)
    {
        if (Guid.TryParse(value, out var guid))
        {
            id = new DependencyId(guid);
            return true;
        }

        id = default;
        return false;
    }

    public int CompareTo(DependencyId other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString("D");
}
