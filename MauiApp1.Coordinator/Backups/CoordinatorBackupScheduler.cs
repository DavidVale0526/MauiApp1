using MauiApp1.Application.Backups;

namespace MauiApp1.Coordinator.Backups;

/// <summary>Runs configured USB backups from the coordinator service without an interactive app session.</summary>
public sealed class CoordinatorBackupScheduler(
    string centralDatabasePath,
    ICoordinatorBackupScheduleStore scheduleStore,
    IBackupDestinationProvider destinationProvider,
    ICentralDatabaseBackupService backupService,
    ILogger<CoordinatorBackupScheduler> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan StaleRunThreshold = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try
            {
                await RunDueBackupAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "The scheduled central database backup could not be processed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task RunDueBackupAsync(CancellationToken cancellationToken)
    {
        var settings = await scheduleStore.ReadSettingsAsync(cancellationToken).ConfigureAwait(false);
        if (settings is null || !settings.IsEnabled)
            return;

        var nowLocal = DateTime.Now;
        var occurrence = GetMostRecentOccurrence(settings, nowLocal);
        if (occurrence is null)
            return;

        var previousRun = await scheduleStore.ReadLastRunAsync(cancellationToken).ConfigureAwait(false);
        if (previousRun is not null && string.Equals(previousRun.OccurrenceKey, occurrence.Value.Key, StringComparison.Ordinal))
        {
            if (previousRun.Result != BackupScheduleRunResult.InProgress ||
                DateTimeOffset.UtcNow - previousRun.LastAttemptedAtUtc < StaleRunThreshold)
                return;
        }

        var startedAt = DateTimeOffset.UtcNow;
        await scheduleStore.SaveLastRunAsync(new BackupScheduleRunStatus(
            occurrence.Value.Key,
            BackupScheduleRunResult.InProgress,
            startedAt,
            null,
            settings.TargetVolumeId,
            "El coordinador está creando y verificando el respaldo programado.",
            null), cancellationToken).ConfigureAwait(false);

        try
        {
            if (string.IsNullOrWhiteSpace(settings.TargetVolumeId))
            {
                await SaveResultAsync(occurrence.Value.Key, BackupScheduleRunResult.Omitted,
                    startedAt, settings.TargetVolumeId,
                    "Se omitió el respaldo: no hay una memoria USB configurada.", null, cancellationToken).ConfigureAwait(false);
                return;
            }

            var destination = await destinationProvider.ResolveAsync(settings.TargetVolumeId, cancellationToken)
                .ConfigureAwait(false);
            if (destination is null)
            {
                await SaveResultAsync(occurrence.Value.Key, BackupScheduleRunResult.Omitted,
                    startedAt, settings.TargetVolumeId,
                    "Se omitió este intento: la memoria USB configurada no está conectada. Se volverá a intentar en la siguiente fecha programada.",
                    null, cancellationToken).ConfigureAwait(false);
                logger.LogWarning("Scheduled backup {OccurrenceKey} was omitted because its USB volume is unavailable.", occurrence.Value.Key);
                return;
            }

            var retentionGroup = settings.Frequency switch
            {
                BackupScheduleFrequency.Weekly => BackupRetentionGroup.Weekly,
                BackupScheduleFrequency.Monthly => BackupRetentionGroup.Monthly,
                _ => BackupRetentionGroup.Daily
            };
            var backup = await backupService.CreateAsync(centralDatabasePath, destination, cancellationToken, retentionGroup)
                .ConfigureAwait(false);
            var retentionApplied = true;
            try
            {
                var retentionLimit = retentionGroup switch
                {
                    BackupRetentionGroup.Weekly => 12,
                    BackupRetentionGroup.Monthly => 12,
                    _ => 30
                };
                await backupService.ApplyRetentionAsync(destination, retentionGroup, retentionLimit, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception retentionException) when (retentionException is not OperationCanceledException)
            {
                retentionApplied = false;
                logger.LogWarning(retentionException,
                    "Backup {BackupFileName} succeeded, but scheduled retention could not be applied.",
                    Path.GetFileName(backup.DatabasePath));
            }
            await SaveResultAsync(occurrence.Value.Key, BackupScheduleRunResult.Completed,
                startedAt, settings.TargetVolumeId,
                $"Respaldo creado y verificado: {Path.GetFileName(backup.DatabasePath)}. " +
                (retentionApplied
                    ? "Se aplicó la retención configurada: hasta 30 diarios o 12 semanales/mensuales del mismo tipo."
                    : "No se pudo aplicar la retención; las copias anteriores se conservaron."),
                Path.GetFileName(backup.DatabasePath),
                cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Scheduled backup {OccurrenceKey} completed as {BackupFileName}.",
                occurrence.Value.Key, Path.GetFileName(backup.DatabasePath));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Scheduled backup {OccurrenceKey} failed.", occurrence.Value.Key);
            await SaveResultAsync(occurrence.Value.Key, BackupScheduleRunResult.Failed,
                startedAt, settings.TargetVolumeId,
                "Falló el respaldo programado. Revisa que la USB tenga espacio y siga conectada; puedes crear una copia manual.",
                null, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SaveResultAsync(
        string occurrenceKey,
        BackupScheduleRunResult result,
        DateTimeOffset attemptedAt,
        string? volumeId,
        string message,
        string? backupFileName,
        CancellationToken cancellationToken)
    {
        await scheduleStore.SaveLastRunAsync(new BackupScheduleRunStatus(
            occurrenceKey, result, attemptedAt, DateTimeOffset.UtcNow, volumeId, message, backupFileName),
            cancellationToken).ConfigureAwait(false);
    }

    private static (string Key, DateTime ScheduledAt)? GetMostRecentOccurrence(
        BackupScheduleSettings settings,
        DateTime nowLocal)
    {
        var scheduledTime = settings.LocalTime.ToTimeSpan();
        DateTime occurrenceDate;

        switch (settings.Frequency)
        {
            case BackupScheduleFrequency.Daily:
                occurrenceDate = nowLocal.Date;
                if (occurrenceDate.Add(scheduledTime) > nowLocal)
                    return null;
                return ($"daily:{occurrenceDate:yyyyMMdd}", occurrenceDate.Add(scheduledTime));

            case BackupScheduleFrequency.Weekly:
                var daysBack = ((int)nowLocal.DayOfWeek - (int)settings.WeeklyDay + 7) % 7;
                occurrenceDate = nowLocal.Date.AddDays(-daysBack);
                if (occurrenceDate.Add(scheduledTime) > nowLocal)
                    return null;
                return ($"weekly:{occurrenceDate:yyyyMMdd}", occurrenceDate.Add(scheduledTime));

            case BackupScheduleFrequency.Monthly:
                occurrenceDate = new DateTime(nowLocal.Year, nowLocal.Month, settings.MonthlyDay);
                if (occurrenceDate.Add(scheduledTime) > nowLocal)
                    return null;
                return ($"monthly:{occurrenceDate:yyyyMM}", occurrenceDate.Add(scheduledTime));

            default:
                return null;
        }
    }
}
