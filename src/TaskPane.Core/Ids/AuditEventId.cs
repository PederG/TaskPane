namespace TaskPane.Core.Ids;

/// <summary>Stable UUID identifier for an audit / history event.</summary>
public readonly record struct AuditEventId(Guid Value) : IComparable<AuditEventId>
{
    public static AuditEventId New() => new(Guid.NewGuid());

    public static AuditEventId Parse(string value) => new(Guid.Parse(value));

    public static bool TryParse(string? value, out AuditEventId id)
    {
        if (Guid.TryParse(value, out var guid))
        {
            id = new AuditEventId(guid);
            return true;
        }

        id = default;
        return false;
    }

    public int CompareTo(AuditEventId other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString("D");
}
