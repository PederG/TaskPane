using System.Text.Json;
using Microsoft.Extensions.Logging;
using TaskPane.Application.Models;
using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using TaskPane.Core.Errors;
using TaskPane.Core.Ids;
using TaskPane.Core.Interfaces;
using TaskPane.Core.Time;
using TaskPane.Core.Validation;

namespace TaskPane.Application.Services;

public sealed class TimeTrackingService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly ITimeSessionRepository _sessions;
    private readonly ITaskRepository _tasks;
    private readonly ISettingsRepository _settings;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly ILogger<TimeTrackingService> _logger;

    public TimeTrackingService(
        ITimeSessionRepository sessions,
        ITaskRepository tasks,
        ISettingsRepository settings,
        IUnitOfWork unitOfWork,
        IClock clock,
        ILogger<TimeTrackingService> logger)
    {
        _sessions = sessions;
        _tasks = tasks;
        _settings = settings;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _logger = logger;
    }

    public Task<TimeSession?> GetActiveSessionAsync(CancellationToken cancellationToken = default)
        => _sessions.GetOpenSessionAsync(cancellationToken);

    public async Task<TimeSession> StartOrSwitchAsync(TaskId taskId, CancellationToken cancellationToken = default)
    {
        var task = await RequireTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
        var now = _clock.UtcNow;
        TimeSession? created = null;

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var open = await _sessions.GetOpenSessionAsync(ct).ConfigureAwait(false);
            if (open is not null)
            {
                if (open.TaskId.Equals(taskId))
                {
                    created = open;
                    return;
                }

                CloseSession(open, now, SessionEndReason.Switched);
                await _sessions.UpdateAsync(open, ct).ConfigureAwait(false);
            }

            created = NewOpenSession(task.Id, now);
            await _sessions.InsertAsync(created, ct).ConfigureAwait(false);
            await ClearPendingResumeAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Timer active on task {TaskId}", taskId);
        return created!;
    }

    public async Task<TimeSession?> StopAsync(CancellationToken cancellationToken = default)
    {
        TimeSession? closed = null;
        var now = _clock.UtcNow;

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var open = await _sessions.GetOpenSessionAsync(ct).ConfigureAwait(false);
            if (open is null)
            {
                return;
            }

            CloseSession(open, now, SessionEndReason.Stopped);
            await _sessions.UpdateAsync(open, ct).ConfigureAwait(false);
            closed = open;
            await ClearPendingResumeAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        return closed;
    }

    /// <summary>Clicking the active timer stops; otherwise starts/switches.</summary>
    public async Task<TimeSession?> ToggleAsync(TaskId taskId, CancellationToken cancellationToken = default)
    {
        var open = await _sessions.GetOpenSessionAsync(cancellationToken).ConfigureAwait(false);
        if (open is not null && open.TaskId.Equals(taskId))
        {
            return await StopAsync(cancellationToken).ConfigureAwait(false);
        }

        return await StartOrSwitchAsync(taskId, cancellationToken).ConfigureAwait(false);
    }

    public async Task EndActiveForTaskAsync(
        TaskId taskId,
        SessionEndReason reason,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var open = await _sessions.GetOpenSessionAsync(ct).ConfigureAwait(false);
            if (open is null || !open.TaskId.Equals(taskId))
            {
                return;
            }

            CloseSession(open, now, reason);
            await _sessions.UpdateAsync(open, ct).ConfigureAwait(false);
            await ClearPendingResumeAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task EndActiveIfAnyAsync(
        SessionEndReason reason,
        bool rememberForResume,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var open = await _sessions.GetOpenSessionAsync(ct).ConfigureAwait(false);
            if (open is null)
            {
                return;
            }

            var taskId = open.TaskId;
            CloseSession(open, now, reason);
            await _sessions.UpdateAsync(open, ct).ConfigureAwait(false);

            if (rememberForResume)
            {
                var settings = await GetSettingsInternalAsync(ct).ConfigureAwait(false);
                settings.PendingResumeTaskId = taskId.ToString();
                settings.PendingResumeReason = reason.ToString();
                await SaveSettingsInternalAsync(settings, ct).ConfigureAwait(false);
            }
            else
            {
                await ClearPendingResumeAsync(ct).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleIdleAsync(
        TimeSpan idleDuration,
        IdleResolutionChoice choice,
        CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(cancellationToken).ConfigureAwait(false);
        var threshold = TimeSpan.FromMinutes(settings.IdleThresholdMinutes);
        if (idleDuration < threshold)
        {
            return;
        }

        var now = _clock.UtcNow;
        var idleStart = now - idleDuration;

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var open = await _sessions.GetOpenSessionAsync(ct).ConfigureAwait(false);
            if (open is null)
            {
                return;
            }

            var taskId = open.TaskId;
            var endAt = open.StartedUtc > idleStart ? open.StartedUtc : idleStart;
            CloseSession(open, endAt, SessionEndReason.Idle);
            await _sessions.UpdateAsync(open, ct).ConfigureAwait(false);

            if (choice == IdleResolutionChoice.DiscardIdle)
            {
                // Already closed at idle onset — idle interval not charged.
                settings.PendingResumeTaskId = null;
                settings.PendingResumeReason = null;
            }
            else
            {
                settings.PendingResumeTaskId = taskId.ToString();
                settings.PendingResumeReason = SessionEndReason.Idle.ToString();
            }

            await SaveSettingsInternalAsync(settings, ct).ConfigureAwait(false);

            if (choice == IdleResolutionChoice.KeepAndResume)
            {
                var resumed = NewOpenSession(taskId, now);
                await _sessions.InsertAsync(resumed, ct).ConfigureAwait(false);
                settings.PendingResumeTaskId = null;
                settings.PendingResumeReason = null;
                await SaveSettingsInternalAsync(settings, ct).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TimeSession?> RecoverOpenSessionAsync(
        SessionRecoveryChoice choice,
        DateTimeOffset? knownCloseUtc = null,
        CancellationToken cancellationToken = default)
    {
        TimeSession? result = null;
        var now = _clock.UtcNow;

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var open = await _sessions.GetOpenSessionAsync(ct).ConfigureAwait(false);
            if (open is null)
            {
                return;
            }

            switch (choice)
            {
                case SessionRecoveryChoice.Discard:
                    open.EndedUtc = open.StartedUtc;
                    open.IsDiscarded = true;
                    open.EndReason = SessionEndReason.Recovered;
                    open.OpenSlot = null;
                    open.UpdatedUtc = now;
                    break;
                case SessionRecoveryChoice.CloseAtKnownTime:
                    var closeAt = knownCloseUtc ?? open.UpdatedUtc;
                    if (closeAt < open.StartedUtc)
                    {
                        closeAt = open.StartedUtc;
                    }

                    if (closeAt > now)
                    {
                        closeAt = now;
                    }

                    CloseSession(open, closeAt, SessionEndReason.Recovered);
                    break;
                case SessionRecoveryChoice.CloseNow:
                    CloseSession(open, now, SessionEndReason.Recovered);
                    break;
                default:
                    throw new BusinessException(ValidationCodes.InvalidSessionRange, "Unknown recovery choice.");
            }

            await _sessions.UpdateAsync(open, ct).ConfigureAwait(false);
            result = open;
        }, cancellationToken).ConfigureAwait(false);

        return result;
    }

    public async Task ResumePendingAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(cancellationToken).ConfigureAwait(false);
        if (settings.PendingResumeTaskId is null
            || !TaskId.TryParse(settings.PendingResumeTaskId, out var taskId))
        {
            return;
        }

        await StartOrSwitchAsync(taskId, cancellationToken).ConfigureAwait(false);
    }

    public async Task ClearPendingResumeOfferAsync(CancellationToken cancellationToken = default)
    {
        await _unitOfWork.ExecuteInTransactionAsync(
            ClearPendingResumeAsync,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<TimeTotals> GetTotalsAsync(TaskId taskId, CancellationToken cancellationToken = default)
    {
        var task = await RequireTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
        var now = _clock.UtcNow;
        var directSessions = await _sessions.ListForTaskAsync(taskId, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var direct = SessionDuration.DirectTotal(directSessions, now);

        if (!task.IsRoot)
        {
            return new TimeTotals { TaskId = taskId, Direct = direct, RolledUp = direct };
        }

        var children = await _tasks.ListChildrenAsync(taskId, includeDeleted: false, cancellationToken)
            .ConfigureAwait(false);
        var childIds = children.Select(c => c.Id).ToList();
        var childSessions = childIds.Count == 0
            ? []
            : await _sessions.ListForTasksAsync(childIds, cancellationToken: cancellationToken).ConfigureAwait(false);
        var groups = childIds.Select(id => childSessions.Where(s => s.TaskId.Equals(id)));
        var rolled = SessionDuration.RolledUpTotal(directSessions, groups, now);
        return new TimeTotals { TaskId = taskId, Direct = direct, RolledUp = rolled };
    }

    public async Task<TimeReport> GetTodayReportAsync(
        TimeZoneInfo? timeZone = null,
        CancellationToken cancellationToken = default)
    {
        var tz = timeZone ?? TimeZoneInfo.Local;
        var nowLocal = TimeZoneInfo.ConvertTime(_clock.UtcNow, tz);
        var startLocal = new DateTimeOffset(nowLocal.Year, nowLocal.Month, nowLocal.Day, 0, 0, 0, nowLocal.Offset);
        var endLocal = startLocal.AddDays(1);
        return await BuildReportAsync("Today", startLocal, endLocal, tz, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TimeReport> GetWeekReportAsync(
        TimeZoneInfo? timeZone = null,
        CancellationToken cancellationToken = default)
    {
        var tz = timeZone ?? TimeZoneInfo.Local;
        var nowLocal = TimeZoneInfo.ConvertTime(_clock.UtcNow, tz);
        var diff = ((int)nowLocal.DayOfWeek + 6) % 7; // Monday-start week
        var startLocal = new DateTimeOffset(nowLocal.Year, nowLocal.Month, nowLocal.Day, 0, 0, 0, nowLocal.Offset)
            .AddDays(-diff);
        var endLocal = startLocal.AddDays(7);
        return await BuildReportAsync("This week", startLocal, endLocal, tz, cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<TimeSession>> ListSessionsForTaskAsync(
        TaskId taskId,
        CancellationToken cancellationToken = default)
        => _sessions.ListForTaskAsync(taskId, includeDiscarded: true, cancellationToken);

    public async Task<TimeSession> AdjustSessionAsync(
        SessionEditRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.EndedUtc < request.StartedUtc)
        {
            throw new BusinessException(
                ValidationCodes.InvalidSessionRange,
                "Session end must be at or after start.");
        }

        var session = await _sessions.GetByIdAsync(request.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new BusinessException(ValidationCodes.SessionNotFound, "Session was not found.");

        if (session.IsOpen)
        {
            throw new BusinessException(
                ValidationCodes.SessionNotOpen,
                "Stop the active timer before editing it, or recover it first.");
        }

        var now = _clock.UtcNow;
        session.StartedUtc = request.StartedUtc;
        session.EndedUtc = request.EndedUtc;
        session.IsDiscarded = request.IsDiscarded;
        session.EndReason = SessionEndReason.ManualEdit;
        session.OpenSlot = null;
        session.UpdatedUtc = now;

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await _sessions.UpdateAsync(session, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        return session;
    }

    public async Task<TimeSession> AddManualSessionAsync(
        ManualSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.EndedUtc < request.StartedUtc)
        {
            throw new BusinessException(
                ValidationCodes.InvalidSessionRange,
                "Session end must be at or after start.");
        }

        await RequireTaskAsync(request.TaskId, cancellationToken).ConfigureAwait(false);
        var now = _clock.UtcNow;
        var session = new TimeSession
        {
            Id = SessionId.New(),
            TaskId = request.TaskId,
            StartedUtc = request.StartedUtc,
            EndedUtc = request.EndedUtc,
            EndReason = SessionEndReason.ManualEdit,
            IsDiscarded = false,
            OpenSlot = null,
            CreatedUtc = now,
            UpdatedUtc = now
        };

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await _sessions.InsertAsync(session, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        return session;
    }

    public async Task DiscardSessionAsync(SessionId id, CancellationToken cancellationToken = default)
    {
        var session = await _sessions.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new BusinessException(ValidationCodes.SessionNotFound, "Session was not found.");

        if (session.IsOpen)
        {
            throw new BusinessException(
                ValidationCodes.SessionNotOpen,
                "Cannot discard an open session; stop or recover it first.");
        }

        session.IsDiscarded = true;
        session.EndReason = SessionEndReason.ManualEdit;
        session.UpdatedUtc = _clock.UtcNow;

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await _sessions.UpdateAsync(session, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TimeTrackingSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
        => await GetSettingsInternalAsync(cancellationToken).ConfigureAwait(false);

    public async Task SaveIdleThresholdAsync(int minutes, CancellationToken cancellationToken = default)
    {
        if (minutes is < 1 or > 240)
        {
            throw new BusinessException(
                ValidationCodes.IdleThresholdInvalid,
                "Idle threshold must be between 1 and 240 minutes.");
        }

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var settings = await GetSettingsInternalAsync(ct).ConfigureAwait(false);
            settings.IdleThresholdMinutes = minutes;
            await SaveSettingsInternalAsync(settings, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TimeReport> BuildReportAsync(
        string label,
        DateTimeOffset startLocal,
        DateTimeOffset endLocal,
        TimeZoneInfo tz,
        CancellationToken cancellationToken)
    {
        var startUtc = startLocal.ToUniversalTime();
        var endUtc = endLocal.ToUniversalTime();
        var sessions = await _sessions.ListInRangeAsync(startUtc, endUtc, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var clipped = new Dictionary<TaskId, TimeSpan>();
        foreach (var session in sessions)
        {
            var end = session.EndedUtc ?? _clock.UtcNow;
            var overlapStart = session.StartedUtc > startUtc ? session.StartedUtc : startUtc;
            var overlapEnd = end < endUtc ? end : endUtc;
            if (overlapEnd <= overlapStart)
            {
                continue;
            }

            clipped.TryGetValue(session.TaskId, out var existing);
            clipped[session.TaskId] = existing + (overlapEnd - overlapStart);
        }

        var rows = new List<TimeReportRow>();
        var total = TimeSpan.Zero;
        foreach (var (taskId, span) in clipped.OrderByDescending(kv => kv.Value))
        {
            var task = await _tasks.GetByIdAsync(taskId, cancellationToken).ConfigureAwait(false);
            rows.Add(new TimeReportRow
            {
                TaskId = taskId,
                Title = task?.Title ?? "(missing task)",
                Direct = span
            });
            total += span;
        }

        return new TimeReport
        {
            Label = label,
            RangeStartLocal = startLocal,
            RangeEndLocal = endLocal,
            Total = total,
            Rows = rows
        };
    }

    private async Task<TaskItem> RequireTaskAsync(TaskId id, CancellationToken cancellationToken)
    {
        var task = await _tasks.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        TaskHierarchyValidator.EnsureExists(task, id);
        return task!;
    }

    private static TimeSession NewOpenSession(TaskId taskId, DateTimeOffset now) => new()
    {
        Id = SessionId.New(),
        TaskId = taskId,
        StartedUtc = now,
        EndedUtc = null,
        EndReason = null,
        IsDiscarded = false,
        OpenSlot = 1,
        CreatedUtc = now,
        UpdatedUtc = now
    };

    private static void CloseSession(TimeSession session, DateTimeOffset endedUtc, SessionEndReason reason)
    {
        if (endedUtc < session.StartedUtc)
        {
            endedUtc = session.StartedUtc;
        }

        session.EndedUtc = endedUtc;
        session.EndReason = reason;
        session.OpenSlot = null;
        session.UpdatedUtc = endedUtc;
    }

    private async Task<TimeTrackingSettings> GetSettingsInternalAsync(CancellationToken cancellationToken)
    {
        var raw = await _settings.GetRawAsync(TimeTrackingSettings.SettingsKey, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new TimeTrackingSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<TimeTrackingSettings>(raw, JsonOptions) ?? new TimeTrackingSettings();
        }
        catch (JsonException)
        {
            return new TimeTrackingSettings();
        }
    }

    private Task SaveSettingsInternalAsync(TimeTrackingSettings settings, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        return _settings.SetRawAsync(TimeTrackingSettings.SettingsKey, json, cancellationToken);
    }

    private async Task ClearPendingResumeAsync(CancellationToken cancellationToken)
    {
        var settings = await GetSettingsInternalAsync(cancellationToken).ConfigureAwait(false);
        if (settings.PendingResumeTaskId is null && settings.PendingResumeReason is null)
        {
            return;
        }

        settings.PendingResumeTaskId = null;
        settings.PendingResumeReason = null;
        await SaveSettingsInternalAsync(settings, cancellationToken).ConfigureAwait(false);
    }
}
