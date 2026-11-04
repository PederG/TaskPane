using System.Text.Json;
using Microsoft.Extensions.Logging;
using TaskPane.Core.Entities;
using TaskPane.Core.Enums;
using TaskPane.Core.Errors;
using TaskPane.Core.Ids;
using TaskPane.Core.Interfaces;
using TaskPane.Core.Reminders;

namespace TaskPane.Application.Services;

public sealed class ReminderService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly IReminderRepository _reminders;
    private readonly ITaskRepository _tasks;
    private readonly ISettingsRepository _settings;
    private readonly INotificationPresenter _notifications;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly ILogger<ReminderService> _logger;

    public ReminderService(
        IReminderRepository reminders,
        ITaskRepository tasks,
        ISettingsRepository settings,
        INotificationPresenter notifications,
        IUnitOfWork unitOfWork,
        IClock clock,
        ILogger<ReminderService> logger)
    {
        _reminders = reminders;
        _tasks = tasks;
        _settings = settings;
        _notifications = notifications;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _logger = logger;
    }

    public static string BuildDeliveryKey(ReminderId id, DateTimeOffset dueUtc)
        => $"{id}:{dueUtc.UtcDateTime:O}";

    public async Task<Reminder> ScheduleAsync(
        TaskId taskId,
        DateTimeOffset dueUtc,
        CancellationToken cancellationToken = default)
    {
        var task = await _tasks.GetByIdAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (task is null || task.IsDeleted)
        {
            throw new BusinessException(ValidationCodes.TaskNotFound, $"Task {taskId} was not found.");
        }

        if (dueUtc == default)
        {
            throw new BusinessException(ValidationCodes.InvalidReminder, "Reminder due time is required.");
        }

        var id = ReminderId.New();
        var due = dueUtc.ToUniversalTime();
        var reminder = new Reminder
        {
            Id = id,
            TaskId = taskId,
            DueUtc = due,
            Kind = ReminderKind.Absolute,
            DeliveryKey = BuildDeliveryKey(id, due),
            CreatedUtc = _clock.UtcNow
        };

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var existing = await _reminders.GetByDeliveryKeyAsync(reminder.DeliveryKey, ct).ConfigureAwait(false);
            if (existing is not null)
            {
                throw new BusinessException(ValidationCodes.InvalidReminder, "Duplicate reminder delivery key.");
            }

            await _reminders.InsertAsync(reminder, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        return reminder;
    }

    public async Task<IReadOnlyList<Reminder>> ListForTaskAsync(
        TaskId taskId,
        CancellationToken cancellationToken = default)
        => await _reminders.ListForTaskAsync(taskId, cancellationToken).ConfigureAwait(false);

    public async Task DismissAsync(ReminderId id, CancellationToken cancellationToken = default)
    {
        var reminder = await RequireAsync(id, cancellationToken).ConfigureAwait(false);
        reminder.DismissedUtc = _clock.UtcNow;
        await _reminders.UpdateAsync(reminder, cancellationToken).ConfigureAwait(false);
    }

    public async Task SnoozeAsync(
        ReminderId id,
        TimeSpan? duration = null,
        CancellationToken cancellationToken = default)
    {
        var reminder = await RequireAsync(id, cancellationToken).ConfigureAwait(false);
        var settings = await GetSettingsAsync(cancellationToken).ConfigureAwait(false);
        var minutes = duration?.TotalMinutes
                      ?? (settings.DefaultSnoozeMinutes > 0 ? settings.DefaultSnoozeMinutes : 15);
        if (minutes <= 0)
        {
            minutes = 15;
        }

        var until = _clock.UtcNow.AddMinutes(minutes);
        reminder.SnoozedUntilUtc = until;
        reminder.DueUtc = until;
        reminder.DeliveredUtc = null;
        reminder.DismissedUtc = null;
        reminder.DeliveryKey = BuildDeliveryKey(reminder.Id, until);
        await _reminders.UpdateAsync(reminder, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ReminderSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var raw = await _settings.GetRawAsync(ReminderSettings.SettingsKey, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new ReminderSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<ReminderSettings>(raw, JsonOptions) ?? new ReminderSettings();
        }
        catch (JsonException)
        {
            return new ReminderSettings();
        }
    }

    public async Task SaveSettingsAsync(ReminderSettings settings, CancellationToken cancellationToken = default)
    {
        if (!TimeOnly.TryParse(settings.QuietStartLocal, out _)
            || !TimeOnly.TryParse(settings.QuietEndLocal, out _))
        {
            throw new BusinessException(ValidationCodes.QuietHoursInvalid, "Quiet hours must be HH:mm.");
        }

        if (settings.DefaultSnoozeMinutes < 1)
        {
            throw new BusinessException(ValidationCodes.QuietHoursInvalid, "Snooze minutes must be >= 1.");
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await _settings.SetRawAsync(ReminderSettings.SettingsKey, json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Delivers due reminders while the tray app is running.
    /// Quiet hours defer without marking delivered; restart uses DeliveryKey dedup.
    /// </summary>
    public async Task<IReadOnlyList<NotificationDeliveryStatus>> ProcessDueAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(cancellationToken).ConfigureAwait(false);
        var now = _clock.UtcNow;
        var due = await _reminders.ListDueAsync(now, cancellationToken).ConfigureAwait(false);
        var results = new List<NotificationDeliveryStatus>();

        foreach (var reminder in due)
        {
            if (reminder.IsDismissed)
            {
                results.Add(NotificationDeliveryStatus.SkippedDismissed);
                continue;
            }

            if (reminder.IsDelivered)
            {
                results.Add(NotificationDeliveryStatus.SkippedAlreadyDelivered);
                continue;
            }

            if (QuietHoursEvaluator.IsInQuietHours(settings, now))
            {
                results.Add(NotificationDeliveryStatus.DeferredQuietHours);
                continue;
            }

            var task = await _tasks.GetByIdAsync(reminder.TaskId, cancellationToken).ConfigureAwait(false);
            if (task is null || task.IsDeleted || task.IsCompleted)
            {
                reminder.DismissedUtc = now;
                await _reminders.UpdateAsync(reminder, cancellationToken).ConfigureAwait(false);
                results.Add(NotificationDeliveryStatus.SkippedDismissed);
                continue;
            }

            // Dedup across restarts: if another row already used this key as delivered, skip.
            var byKey = await _reminders.GetByDeliveryKeyAsync(reminder.DeliveryKey, cancellationToken)
                .ConfigureAwait(false);
            if (byKey is not null && byKey.IsDelivered && !byKey.Id.Equals(reminder.Id))
            {
                reminder.DeliveredUtc = byKey.DeliveredUtc;
                await _reminders.UpdateAsync(reminder, cancellationToken).ConfigureAwait(false);
                results.Add(NotificationDeliveryStatus.SkippedAlreadyDelivered);
                continue;
            }

            var status = await _notifications.ShowAsync(new ReminderNotification
            {
                ReminderId = reminder.Id,
                TaskId = task.Id,
                Title = task.Title, // notes intentionally omitted
                DeliveryKey = reminder.DeliveryKey,
                DueUtc = reminder.DueUtc
            }, cancellationToken).ConfigureAwait(false);

            if (status is NotificationDeliveryStatus.Delivered or NotificationDeliveryStatus.FallbackInApp)
            {
                reminder.DeliveredUtc = now;
                await _reminders.UpdateAsync(reminder, cancellationToken).ConfigureAwait(false);
            }

            results.Add(status);
            _logger.LogInformation("Reminder {ReminderId} delivery status {Status}", reminder.Id, status);
        }

        return results;
    }

    private async Task<Reminder> RequireAsync(ReminderId id, CancellationToken cancellationToken)
    {
        var reminder = await _reminders.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (reminder is null)
        {
            throw new BusinessException(ValidationCodes.ReminderNotFound, $"Reminder {id} was not found.");
        }

        return reminder;
    }
}
