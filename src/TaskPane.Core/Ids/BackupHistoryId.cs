namespace TaskPane.Core.Ids;

public readonly record struct BackupHistoryId(Guid Value)
{
    public static BackupHistoryId New() => new(Guid.NewGuid());

    public static BackupHistoryId Parse(string value) => new(Guid.Parse(value));

    public static bool TryParse(string? value, out BackupHistoryId id)
    {
        if (Guid.TryParse(value, out var guid))
        {
            id = new BackupHistoryId(guid);
            return true;
        }

        id = default;
        return false;
    }

    public override string ToString() => Value.ToString("D");
}
