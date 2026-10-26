namespace MauiApp1.Application.Backups;

/// <summary>A currently mounted removable volume that can receive a local backup.</summary>
public sealed record BackupDestination(
    string VolumeId,
    string DisplayName,
    string MountPath,
    long AvailableBytes,
    long TotalBytes);

/// <summary>Finds and resolves removable local volumes without coupling the UI to Windows APIs.</summary>
public interface IBackupDestinationProvider
{
    Task<IReadOnlyList<BackupDestination>> GetAvailableAsync(CancellationToken cancellationToken = default);

    Task<BackupDestination?> ResolveAsync(
        string volumeId,
        CancellationToken cancellationToken = default);
}
