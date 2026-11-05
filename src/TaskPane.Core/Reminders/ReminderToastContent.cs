using System.Net;
using System.Text;
using TaskPane.Core.Ids;

namespace TaskPane.Core.Reminders;

/// <summary>
/// Builds Windows toast XML for unpackaged desktop delivery.
/// Never embeds task notes — title and branding only.
/// </summary>
public static class ReminderToastContent
{
    public static string BuildXml(
        string appDisplayName,
        string taskTitle,
        ReminderId reminderId,
        TaskId taskId)
    {
        var brand = SanitizeLine(appDisplayName, fallback: "TaskPane");
        var title = SanitizeLine(taskTitle, fallback: "Reminder");
        var reminder = reminderId.Value.ToString("D");
        var task = taskId.Value.ToString("D");
        var launch = BuildArguments("open", reminder, task);

        var sb = new StringBuilder(512);
        sb.Append("<toast launch=\"").Append(XmlEscape(launch)).Append("\">");
        sb.Append("<visual><binding template=\"ToastGeneric\">");
        sb.Append("<text>").Append(XmlEscape(brand)).Append("</text>");
        sb.Append("<text>").Append(XmlEscape(title)).Append("</text>");
        sb.Append("<text>Reminder</text>");
        sb.Append("</binding></visual>");
        sb.Append("<actions>");
        sb.Append("<action activationType=\"foreground\" content=\"Snooze\" arguments=\"")
            .Append(XmlEscape(BuildArguments("snooze", reminder, task)))
            .Append("\"/>");
        sb.Append("<action activationType=\"foreground\" content=\"Dismiss\" arguments=\"")
            .Append(XmlEscape(BuildArguments("dismiss", reminder, task)))
            .Append("\"/>");
        sb.Append("</actions></toast>");
        return sb.ToString();
    }

    public static string BuildArguments(string action, string reminderId, string taskId)
        => $"action={Uri.EscapeDataString(action)}&reminderId={Uri.EscapeDataString(reminderId)}&taskId={Uri.EscapeDataString(taskId)}";

    public static bool TryParseArguments(
        string? arguments,
        out string action,
        out Guid reminderId,
        out Guid taskId)
    {
        action = string.Empty;
        reminderId = Guid.Empty;
        taskId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return false;
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in arguments.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = part.IndexOf('=');
            if (idx <= 0)
            {
                continue;
            }

            var key = Uri.UnescapeDataString(part[..idx]);
            var value = Uri.UnescapeDataString(part[(idx + 1)..]);
            map[key] = value;
        }

        if (!map.TryGetValue("action", out var parsedAction)
            || !map.TryGetValue("reminderId", out var reminderRaw)
            || !map.TryGetValue("taskId", out var taskRaw)
            || !Guid.TryParse(reminderRaw, out reminderId)
            || !Guid.TryParse(taskRaw, out taskId))
        {
            return false;
        }

        action = parsedAction;
        return action is "open" or "snooze" or "dismiss";
    }

    private static string SanitizeLine(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        // Collapse newlines so private note bodies cannot leak as multi-line toast content.
        var single = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (single.Length > 120)
        {
            single = single[..117] + "...";
        }

        return string.IsNullOrWhiteSpace(single) ? fallback : single;
    }

    private static string XmlEscape(string value) => WebUtility.HtmlEncode(value);
}
