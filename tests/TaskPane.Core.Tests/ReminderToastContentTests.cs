using TaskPane.Core.Ids;
using TaskPane.Core.Reminders;

namespace TaskPane.Core.Tests;

public sealed class ReminderToastContentTests
{
    [Fact]
    public void Toast_xml_includes_brand_title_and_actions_but_never_notes()
    {
        var reminderId = ReminderId.New();
        var taskId = TaskId.New();
        const string notes = "SECRET note that must not leak into toast XML";

        var xml = ReminderToastContent.BuildXml("CustomBrand", "Review budget", reminderId, taskId);

        Assert.Contains("CustomBrand", xml, StringComparison.Ordinal);
        Assert.Contains("Review budget", xml, StringComparison.Ordinal);
        Assert.Contains("Snooze", xml, StringComparison.Ordinal);
        Assert.Contains("Dismiss", xml, StringComparison.Ordinal);
        Assert.Contains(reminderId.ToString(), xml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(taskId.ToString(), xml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(notes, xml, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Toast_xml_collapses_newlines_in_title()
    {
        var xml = ReminderToastContent.BuildXml(
            "TaskPane",
            "Line1\nLine2\rLine3",
            ReminderId.New(),
            TaskId.New());

        Assert.DoesNotContain('\n', xml.Replace("<text>Reminder</text>", string.Empty));
        Assert.Contains("Line1 Line2 Line3", xml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("action=open&reminderId={0}&taskId={1}", "open")]
    [InlineData("action=snooze&reminderId={0}&taskId={1}", "snooze")]
    [InlineData("action=dismiss&reminderId={0}&taskId={1}", "dismiss")]
    public void Parses_activation_arguments(string template, string expectedAction)
    {
        var reminderId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var args = string.Format(template, reminderId.ToString("D"), taskId.ToString("D"));

        Assert.True(ReminderToastContent.TryParseArguments(args, out var action, out var parsedReminder, out var parsedTask));
        Assert.Equal(expectedAction, action);
        Assert.Equal(reminderId, parsedReminder);
        Assert.Equal(taskId, parsedTask);
    }

    [Fact]
    public void Rejects_malformed_activation_arguments()
    {
        Assert.False(ReminderToastContent.TryParseArguments("action=open", out _, out _, out _));
        Assert.False(ReminderToastContent.TryParseArguments(null, out _, out _, out _));
        Assert.False(ReminderToastContent.TryParseArguments("action=noop&reminderId=" + Guid.NewGuid() + "&taskId=" + Guid.NewGuid(), out _, out _, out _));
    }
}
