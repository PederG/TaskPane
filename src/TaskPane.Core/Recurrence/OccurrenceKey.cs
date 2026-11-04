namespace TaskPane.Core.Recurrence;

/// <summary>
/// Deterministic occurrence key: local scheduled date, plus time when the rule is timed.
/// Unique together with RecurrenceTemplateId.
/// </summary>
public static class OccurrenceKey
{
    public static string FromLocal(DateOnly localDate, TimeOnly? localTime)
        => localTime is { } time
            ? $"{localDate:yyyy-MM-dd}T{time:HH\\:mm}"
            : localDate.ToString("yyyy-MM-dd");

    public static bool TryParse(string? key, out DateOnly date, out TimeOnly? time)
    {
        date = default;
        time = null;
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var parts = key.Split('T', 2);
        if (!DateOnly.TryParse(parts[0], out date))
        {
            return false;
        }

        if (parts.Length == 2)
        {
            if (!TimeOnly.TryParse(parts[1], out var parsed))
            {
                return false;
            }

            time = parsed;
        }

        return true;
    }
}
