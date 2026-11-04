using TaskPane.Core.Errors;

namespace TaskPane.Core.Validation;

public static class TaskTitle
{
    public static string NormalizeOrThrow(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new BusinessException(ValidationCodes.TitleRequired, "Task title is required.");
        }

        return title.Trim();
    }
}
