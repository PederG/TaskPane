using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TaskPane.Application.Models;
using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using TaskPane.Core.Errors;
using TaskPane.Core.Ids;
using TaskPane.Core.Interfaces;

namespace TaskPane.Application.Services;

public sealed class BackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly IDatabaseBackupRunner _backupRunner;
    private readonly IDocumentsFolderResolver _documents;
    private readonly IBackupHistoryRepository _history;
    private readonly ISettingsRepository _settings;
    private readonly IClock _clock;
    private readonly ILogger<BackupService> _logger;

    public BackupService(
        IDatabaseBackupRunner backupRunner,
        IDocumentsFolderResolver documents,
        IBackupHistoryRepository history,
        ISettingsRepository settings,
        IClock clock,
        ILogger<BackupService> logger)
    {
        _backupRunner = backupRunner;
        _documents = documents;
        _history = history;
        _settings = settings;
        _clock = clock;
        _logger = logger;
    }

    public async Task<BackupSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var raw = await _settings.GetRawAsync(BackupSettings.SettingsKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new BackupSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<BackupSettings>(raw, JsonOptions) ?? new BackupSettings();
        }
        catch (JsonException)
        {
            return new BackupSettings();
        }
    }

    public async Task SaveSettingsAsync(BackupSettings settings, CancellationToken cancellationToken = default)
    {
        if (settings.LocalRetentionCount < 1 || settings.DocumentsRetentionCount < 1)
        {
            throw new BusinessException(
                ValidationCodes.BackupRetentionInvalid,
                "Backup retention counts must be at least 1.");
        }

        if (settings.IntervalMinutes < 0)
        {
            throw new BusinessException(
                ValidationCodes.BackupRetentionInvalid,
                "Backup interval cannot be negative.");
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await _settings.SetRawAsync(BackupSettings.SettingsKey, json, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BackupStatusInfo> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var docs = _documents.Resolve();
        var latest = await _history.GetLatestSuccessfulAsync(cancellationToken).ConfigureAwait(false);

        string status;
        if (latest is null)
        {
            status = docs.IsAvailable
                ? "No successful backup yet."
                : "No successful backup yet. Documents folder is offline — local backups will still work.";
        }
        else if (!docs.IsAvailable)
        {
            status =
                $"Last backup {latest.CreatedUtc.LocalDateTime:g} (local only). Documents offline — will retry when reachable.";
        }
        else if (latest.Result == BackupOutcome.SuccessWithDocuments)
        {
            status = $"Last backup {latest.CreatedUtc.LocalDateTime:g} (local + Documents).";
        }
        else
        {
            status = $"Last local backup {latest.CreatedUtc.LocalDateTime:g}. Documents copy pending/unavailable at that time.";
        }

        return new BackupStatusInfo
        {
            LastSuccessfulUtc = latest?.CreatedUtc,
            LastOutcome = latest?.Result,
            LastLocalPath = latest?.LocalPath,
            LastDocumentsPath = latest?.DocumentsPath,
            DocumentsAvailable = docs.IsAvailable,
            DocumentsIsUnc = docs.IsUnc,
            DocumentsWarning = docs.IsAvailable ? null : docs.Message,
            StatusText = status
        };
    }

    /// <summary>
    /// Creates a consistent local SQLite backup, optionally copies to Documents when reachable,
    /// applies retention, and records history. Never blocks task writes on Documents availability.
    /// </summary>
    public async Task<BackupRunResult> CreateBackupAsync(
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(cancellationToken).ConfigureAwait(false);
        var now = _clock.UtcNow;
        var stamp = now.UtcDateTime.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture);
        var localDir = GetLocalBackupsDirectory();
        Directory.CreateDirectory(localDir);
        var localPath = Path.Combine(localDir, $"tasks-backup-{stamp}-{Guid.NewGuid():N}.db");

        try
        {
            await _backupRunner.BackupLiveToAsync(localPath, cancellationToken).ConfigureAwait(false);
        }
        catch (BusinessException ex)
        {
            var failed = new BackupHistoryEntry
            {
                Id = BackupHistoryId.New(),
                CreatedUtc = now,
                LocalPath = localPath,
                Result = BackupOutcome.Failed,
                ErrorCode = ex.Code,
                Message = ex.Message
            };
            try
            {
                await _history.InsertAsync(failed, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception historyEx)
            {
                _logger.LogWarning(historyEx, "Could not record failed backup history");
            }

            _logger.LogWarning("Backup failed: {Code} {Message}", ex.Code, ex.Message);
            return new BackupRunResult
            {
                Outcome = BackupOutcome.Failed,
                LocalPath = localPath,
                ErrorCode = ex.Code,
                Message = ex.Message
            };
        }

        ApplyRetention(localDir, "tasks-backup-*.db", settings.LocalRetentionCount);

        string? documentsPath = null;
        var docs = _documents.Resolve();
        BackupOutcome outcome;
        string? errorCode = null;
        string message;

        if (docs.IsAvailable && !string.IsNullOrWhiteSpace(docs.Path))
        {
            try
            {
                var docsDir = Path.Combine(docs.Path, "TaskPane", "Backups");
                Directory.CreateDirectory(docsDir);
                documentsPath = Path.Combine(docsDir, Path.GetFileName(localPath));
                // Copy the already-verified local snapshot; never point live DB at Documents.
                File.Copy(localPath, documentsPath, overwrite: false);
                ApplyRetention(docsDir, "tasks-backup-*.db", settings.DocumentsRetentionCount);
                outcome = BackupOutcome.SuccessWithDocuments;
                message = string.IsNullOrWhiteSpace(reason)
                    ? "Backup saved locally and copied to Documents."
                    : $"Backup ({reason}) saved locally and copied to Documents.";
            }
            catch (Exception)
            {
                outcome = BackupOutcome.DocumentsUnavailable;
                errorCode = ValidationCodes.DocumentsUnavailable;
                message =
                    "Local backup succeeded, but Documents copy failed. Will retry when Documents is available.";
                documentsPath = null;
            }
        }
        else
        {
            outcome = BackupOutcome.SuccessLocalOnly;
            errorCode = docs.ErrorCode ?? ValidationCodes.DocumentsUnavailable;
            message = string.IsNullOrWhiteSpace(reason)
                ? "Local backup succeeded. Documents unavailable — queued for later copy via next backup."
                : $"Local backup ({reason}) succeeded. Documents unavailable.";
        }

        await _history.InsertAsync(new BackupHistoryEntry
        {
            Id = BackupHistoryId.New(),
            CreatedUtc = now,
            LocalPath = localPath,
            DocumentsPath = documentsPath,
            Result = outcome == BackupOutcome.DocumentsUnavailable
                ? BackupOutcome.SuccessLocalOnly
                : outcome,
            ErrorCode = errorCode,
            Message = message
        }, cancellationToken).ConfigureAwait(false);

        return new BackupRunResult
        {
            Outcome = outcome,
            LocalPath = localPath,
            DocumentsPath = documentsPath,
            ErrorCode = errorCode,
            Message = message
        };
    }

    public async Task RestoreFromBackupAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
        {
            throw new BusinessException(ValidationCodes.BackupNotFound, "Backup file was not found.");
        }

        // Always keep a safety snapshot of the current live DB before restore.
        var safety = await CreateBackupAsync("pre-restore", cancellationToken).ConfigureAwait(false);
        if (safety.Outcome == BackupOutcome.Failed)
        {
            throw new BusinessException(
                ValidationCodes.RestoreFailed,
                "Could not create a safety backup before restore.");
        }

        await _backupRunner.RestoreBackupIntoLiveAsync(backupPath, cancellationToken).ConfigureAwait(false);
    }

    public string GetRecoveryInstructions()
    {
        var local = GetLocalBackupsDirectory();
        return
            "Recovery: pick a verified tasks-backup-*.db from the local Backups folder " +
            $"({local}) or Documents\\TaskPane\\Backups when online. " +
            "Use Restore to apply it — TaskPane creates a safety backup first. " +
            "Never copy a backup over the live database while the app is running, " +
            "and never place the live database on a redirected Documents/network path.";
    }

    private string GetLocalBackupsDirectory()
    {
        var dataDir = Path.GetDirectoryName(_backupRunner.LiveDatabasePath)
                      ?? throw new InvalidOperationException("Live database path is invalid.");
        var dir = Path.Combine(dataDir, "Backups");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void ApplyRetention(string directory, string pattern, int keep)
    {
        if (keep < 1 || !Directory.Exists(directory))
        {
            return;
        }

        var files = Directory.GetFiles(directory, pattern)
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.CreationTimeUtc)
            .ThenByDescending(f => f.Name)
            .ToList();

        foreach (var stale in files.Skip(keep))
        {
            try
            {
                stale.Delete();
            }
            catch
            {
                // best effort retention
            }
        }
    }
}
