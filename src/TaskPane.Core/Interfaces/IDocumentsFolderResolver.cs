namespace TaskPane.Core.Interfaces;

/// <summary>
/// Resolves the Windows Documents known folder (may be a redirected UNC path)
/// and reports whether it is currently reachable for backups/exports.
/// </summary>
public interface IDocumentsFolderResolver
{
    DocumentsFolderInfo Resolve();
}

public sealed class DocumentsFolderInfo
{
    public required string? Path { get; init; }
    public bool IsAvailable { get; init; }
    public bool IsUnc { get; init; }
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }

    public static DocumentsFolderInfo Unavailable(string errorCode, string message)
        => new()
        {
            Path = null,
            IsAvailable = false,
            IsUnc = false,
            ErrorCode = errorCode,
            Message = message
        };
}
