namespace TaskPane.Core.Ids;

/// <summary>Stable UUID identifier for a tag.</summary>
public readonly record struct TagId(Guid Value) : IComparable<TagId>
{
    public static TagId New() => new(Guid.NewGuid());

    public static TagId Parse(string value) => new(Guid.Parse(value));

    public static bool TryParse(string? value, out TagId id)
    {
        if (Guid.TryParse(value, out var guid))
        {
            id = new TagId(guid);
            return true;
        }

        id = default;
        return false;
    }

    public int CompareTo(TagId other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString("D");
}
