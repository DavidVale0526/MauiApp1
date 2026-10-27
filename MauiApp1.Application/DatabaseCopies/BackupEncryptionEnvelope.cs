namespace MauiApp1.Application.Backups;

/// <summary>Passphrase-protected copy of the random key used to encrypt backup data.</summary>
public sealed record BackupEncryptionEnvelope(
    int Version,
    int KdfIterations,
    string SaltBase64,
    string NonceBase64,
    string WrappedDataKeyBase64,
    string AuthenticationTagBase64);
