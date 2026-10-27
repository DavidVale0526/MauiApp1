namespace MauiApp1.Application.Backups;

public sealed record BackupEncryptionKeyMaterial(byte[] DataKey, BackupEncryptionEnvelope RecoveryEnvelope);

/// <summary>Stores the local copy of the backup key protected by the Windows machine DPAPI.</summary>
public interface ICentralBackupEncryptionKeyStore
{
    Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default);

    Task<BackupEncryptionKeyMaterial?> ReadAsync(CancellationToken cancellationToken = default);

    Task ConfigureAsync(string recoveryPassphrase, CancellationToken cancellationToken = default);

    Task UnlockFromBackupAsync(
        BackupEncryptionEnvelope recoveryEnvelope,
        string recoveryPassphrase,
        CancellationToken cancellationToken = default);
}
