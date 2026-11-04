using System.Text.RegularExpressions;

namespace TaskPane.Core.Logging;

/// <summary>
/// Redacts likely task content from log messages. Prefer structured logging with IDs only;
/// this is a last-line defense for accidental string interpolation.
/// </summary>
public static partial class LogPrivacy
{
    public const string RedactedPlaceholder = "[redacted]";

    /// <summary>
    /// Returns true when a log template or message appears to embed free-form task content
    /// (notes/title payloads) rather than identifiers.
    /// </summary>
    public static bool LooksLikeTaskContent(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        if (message.Contains("Notes=", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Title=", StringComparison.OrdinalIgnoreCase)
            || message.Contains("note body", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Long free-form blobs are treated as content.
        return message.Length > 280 && message.Contains(' ');
    }

    public static string Sanitize(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return string.Empty;
        }

        if (LooksLikeTaskContent(message))
        {
            return RedactedPlaceholder;
        }

        return StripQuotedPayloads().Replace(message, RedactedPlaceholder);
    }

    [GeneratedRegex("""(?i)(title|notes|waiting\s*for)\s*[:=]\s*(".*?"|'.*?'|[^\s;,]+)""")]
    private static partial Regex StripQuotedPayloads();
}
