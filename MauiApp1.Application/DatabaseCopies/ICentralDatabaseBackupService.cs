namespace MauiApp1.Application.Backups;

public sealed record CentralDatabaseBackup(
    Guid BackupId,
    string DatabasePath,
    string MetadataPath,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset CreatedAtLocal,
    long SizeBytes,
    string Sha256,
    string SchemaVersion,
    string ApplicationVersion);

public sealed record BackupValidationResult(
    bool IsValid,
    bool IsSchemaCompatible,
    string FileName,
    DateTimeOffset? CreatedAtUtc,
    long SizeBytes,
    string? BackupSchemaVersion,
    string? CurrentSchemaVersion,
    string Message);

public enum BackupRetentionGroup
{
    Manual,
    Daily,
    Weekly,
    Monthly
}

public interface ICentralDatabaseBackupService
{
    Task<bool> IsEncryptionConfiguredAsync(CancellationToken cancellationToken = default);

    Task ConfigureEncryptionAsync(string recoveryPassphrase, CancellationToken cancellationToken = default);

    Task UnlockEncryptionFromBackupAsync(BackupDestination destination, string backupFileName,
        string recoveryPassphrase, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BackupHistoryEntry>> GetHistoryAsync(
        BackupDestination destination,
        CancellationToken cancellationToken = default);

    Task<CentralDatabaseBackup> CreateAsync(
        string centralDatabasePath,
        BackupDestination destination,
        CancellationToken cancellationToken = default,
        BackupRetentionGroup retentionGroup = BackupRetentionGroup.Manual);

    Task ApplyRetentionAsync(
        BackupDestination destination,
        BackupRetentionGroup retentionGroup,
        int maximumBackups,
        CancellationToken cancellationToken = default);

    Task<BackupValidationResult> ValidateAsync(
        string currentCentralDatabasePath,
        BackupDestination destination,
        string backupFileName,
        CancellationToken cancellationToken = default);
}
