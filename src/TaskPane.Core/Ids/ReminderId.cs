namespace TaskPane.Core.Ids;

public readonly record struct ReminderId(Guid Value) : IComparable<ReminderId>
{
    public static ReminderId New() => new(Guid.NewGuid());

    public static ReminderId Parse(string value) => new(Guid.Parse(value));

    public static bool TryParse(string? value, out ReminderId id)
    {
        if (Guid.TryParse(value, out var guid))
        {
            id = new ReminderId(guid);
            return true;
        }

        id = default;
        return false;
    }

    public int CompareTo(ReminderId other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString("D");
}
