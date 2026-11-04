using TaskPane.Core.Logging;

namespace TaskPane.Core.Tests;

public sealed class LogPrivacyTests
{
    [Fact]
    public void Redacts_title_and_notes_payloads()
    {
        var sanitized = LogPrivacy.Sanitize("Updated Title=\"Secret plan\" Notes=do not leak");
        Assert.Equal(LogPrivacy.RedactedPlaceholder, sanitized);
    }

    [Fact]
    public void Keeps_id_only_operational_messages()
    {
        var message = "Created task 0193a1c0-0000-7000-8000-000000000001 (parent=False)";
        Assert.Equal(message, LogPrivacy.Sanitize(message));
        Assert.False(LogPrivacy.LooksLikeTaskContent(message));
    }
}
