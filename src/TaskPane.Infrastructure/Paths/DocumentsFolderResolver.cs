using TaskPane.Core.Errors;
using TaskPane.Core.Interfaces;

namespace TaskPane.Infrastructure.Paths;

/// <summary>
/// Resolves the Documents known folder via SpecialFolder.MyDocuments (honors Windows folder redirection).
/// Probe reachability without blocking the UI for long; callers should run off the UI thread.
/// </summary>
public sealed class DocumentsFolderResolver : IDocumentsFolderResolver
{
    private readonly Func<string?> _documentsPathFactory;
    private readonly Func<string, bool> _directoryExists;
    private readonly Action<string>? _ensureDirectory;

    public DocumentsFolderResolver()
        : this(
            () => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Directory.Exists,
            path => Directory.CreateDirectory(path))
    {
    }

    /// <summary>Test seam for unavailable / redirected Documents scenarios.</summary>
    public DocumentsFolderResolver(
        Func<string?> documentsPathFactory,
        Func<string, bool> directoryExists,
        Action<string>? ensureDirectory = null)
    {
        _documentsPathFactory = documentsPathFactory;
        _directoryExists = directoryExists;
        _ensureDirectory = ensureDirectory;
    }

    public DocumentsFolderInfo Resolve()
    {
        string? path;
        try
        {
            path = _documentsPathFactory();
        }
        catch (Exception)
        {
            return DocumentsFolderInfo.Unavailable(
                ValidationCodes.DocumentsUnavailable,
                "Documents folder could not be resolved.");
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return DocumentsFolderInfo.Unavailable(
                ValidationCodes.DocumentsUnavailable,
                "Documents known folder is empty or unset.");
        }

        var isUnc = path.StartsWith(@"\\", StringComparison.Ordinal)
                    || path.StartsWith("//", StringComparison.Ordinal);

        try
        {
            if (!_directoryExists(path))
            {
                return new DocumentsFolderInfo
                {
                    Path = path,
                    IsAvailable = false,
                    IsUnc = isUnc,
                    ErrorCode = ValidationCodes.DocumentsUnavailable,
                    Message = "Documents folder is not reachable right now."
                };
            }

            // Probe write capability with a temp marker under the product folder.
            var probeRoot = Path.Combine(path, AppPaths.ProductFolderName);
            _ensureDirectory?.Invoke(probeRoot);
            var probe = Path.Combine(probeRoot, $".probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);

            return new DocumentsFolderInfo
            {
                Path = path,
                IsAvailable = true,
                IsUnc = isUnc
            };
        }
        catch (Exception)
        {
            return new DocumentsFolderInfo
            {
                Path = path,
                IsAvailable = false,
                IsUnc = isUnc,
                ErrorCode = ValidationCodes.DocumentsUnavailable,
                Message = "Documents folder is offline or not writable. Local backups will continue."
            };
        }
    }
}
