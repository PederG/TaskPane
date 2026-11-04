using TaskPane.Core.Errors;

namespace TaskPane.Application.Validation;

public static class DeadlineValidator
{
    public static void Validate(string? localDate, string? localTime)
    {
        if (string.IsNullOrWhiteSpace(localDate))
        {
            if (!string.IsNullOrWhiteSpace(localTime))
            {
                throw new BusinessException(
                    ValidationCodes.InvalidDeadline,
                    "A deadline time requires a deadline date.");
            }

            return;
        }

        if (!DateOnly.TryParse(localDate, out _))
        {
            throw new BusinessException(
                ValidationCodes.InvalidDeadline,
                "Deadline date must be YYYY-MM-DD.");
        }

        if (!string.IsNullOrWhiteSpace(localTime) && !TimeOnly.TryParse(localTime, out _))
        {
            throw new BusinessException(
                ValidationCodes.InvalidDeadline,
                "Deadline time must be a valid local time.");
        }
    }

    public static string? NormalizeDate(string? localDate)
        => string.IsNullOrWhiteSpace(localDate) ? null : DateOnly.Parse(localDate.Trim()).ToString("yyyy-MM-dd");

    public static string? NormalizeTime(string? localTime)
    {
        if (string.IsNullOrWhiteSpace(localTime))
        {
            return null;
        }

        var time = TimeOnly.Parse(localTime.Trim());
        return time.Second == 0
            ? time.ToString("HH:mm")
            : time.ToString("HH:mm:ss");
    }
}
