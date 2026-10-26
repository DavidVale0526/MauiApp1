namespace MauiApp1.Application.Backups;

public enum BackupScheduleFrequency
{
    Daily,
    Weekly,
    Monthly
}

public enum BackupScheduleRunResult
{
    InProgress,
    Completed,
    Omitted,
    Failed
}

public sealed record BackupScheduleSettings(
    bool IsEnabled,
    BackupScheduleFrequency Frequency,
    TimeOnly LocalTime,
    DayOfWeek WeeklyDay,
    int MonthlyDay,
    string? TargetVolumeId);

public sealed record BackupScheduleRunStatus(
    string OccurrenceKey,
    BackupScheduleRunResult Result,
    DateTimeOffset LastAttemptedAtUtc,
    DateTimeOffset? LastFinishedAtUtc,
    string? TargetVolumeId,
    string Message,
    string? BackupFileName);

public interface ICoordinatorBackupScheduleStore
{
    Task<BackupScheduleSettings?> ReadSettingsAsync(CancellationToken cancellationToken = default);

    Task SaveSettingsAsync(BackupScheduleSettings settings, CancellationToken cancellationToken = default);

    Task<BackupScheduleRunStatus?> ReadLastRunAsync(CancellationToken cancellationToken = default);

    Task SaveLastRunAsync(BackupScheduleRunStatus status, CancellationToken cancellationToken = default);
}
