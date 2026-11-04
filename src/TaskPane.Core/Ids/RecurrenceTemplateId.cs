namespace TaskPane.Core.Ids;

public readonly record struct RecurrenceTemplateId(Guid Value) : IComparable<RecurrenceTemplateId>
{
    public static RecurrenceTemplateId New() => new(Guid.NewGuid());

    public static RecurrenceTemplateId Parse(string value) => new(Guid.Parse(value));

    public static bool TryParse(string? value, out RecurrenceTemplateId id)
    {
        if (Guid.TryParse(value, out var guid))
        {
            id = new RecurrenceTemplateId(guid);
            return true;
        }

        id = default;
        return false;
    }

    public int CompareTo(RecurrenceTemplateId other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString("D");
}
