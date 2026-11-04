namespace TaskPane.Core.Ids;

/// <summary>Stable UUID identifier for a time session.</summary>
public readonly record struct SessionId(Guid Value) : IComparable<SessionId>
{
    public static SessionId New() => new(Guid.NewGuid());

    public static SessionId Parse(string value) => new(Guid.Parse(value));

    public static bool TryParse(string? value, out SessionId id)
    {
        if (Guid.TryParse(value, out var guid))
        {
            id = new SessionId(guid);
            return true;
        }

        id = default;
        return false;
    }

    public int CompareTo(SessionId other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString("D");
}
