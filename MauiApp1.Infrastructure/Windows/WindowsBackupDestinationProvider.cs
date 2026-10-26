using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using MauiApp1.Application.Backups;

namespace MauiApp1.Infrastructure.Windows;

/// <summary>Lists mounted Windows removable volumes using their stable volume GUID.</summary>
public sealed class WindowsBackupDestinationProvider : IBackupDestinationProvider
{
    private const int VolumeNameBufferLength = 128;

    public Task<IReadOnlyList<BackupDestination>> GetAvailableAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var destinations = new List<BackupDestination>();

        foreach (var drive in DriveInfo.GetDrives())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (drive.DriveType != DriveType.Removable || !drive.IsReady)
                continue;

            try
            {
                var mountPath = Path.GetPathRoot(drive.Name);
                if (string.IsNullOrWhiteSpace(mountPath))
                    continue;

                var volumeId = GetVolumeId(mountPath);
                if (volumeId is null)
                    continue;

                var label = drive.VolumeLabel;
                var displayName = string.IsNullOrWhiteSpace(label)
                    ? $"Memoria USB ({mountPath.TrimEnd('\\', '/')})"
                    : $"{label} ({mountPath.TrimEnd('\\', '/')})";

                destinations.Add(new BackupDestination(
                    volumeId,
                    displayName,
                    mountPath,
                    drive.AvailableFreeSpace,
                    drive.TotalSize));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                                System.Security.SecurityException or Win32Exception)
            {
                // A removable volume may disappear while Windows is enumerating it.
                // Omit it from this snapshot; the next refresh will detect it again.
            }
        }

        return Task.FromResult<IReadOnlyList<BackupDestination>>(destinations
            .OrderBy(destination => destination.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray());
    }

    public async Task<BackupDestination?> ResolveAsync(
        string volumeId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(volumeId))
            return null;

        var destinations = await GetAvailableAsync(cancellationToken).ConfigureAwait(false);
        return destinations.FirstOrDefault(destination =>
            string.Equals(destination.VolumeId, volumeId, StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetVolumeId(string mountPath)
    {
        var volumeName = new StringBuilder(VolumeNameBufferLength);
        return GetVolumeNameForVolumeMountPoint(mountPath, volumeName, VolumeNameBufferLength)
            ? volumeName.ToString().TrimEnd('\\')
            : null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true,
        EntryPoint = "GetVolumeNameForVolumeMountPointW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPoint(
        string volumeMountPoint,
        StringBuilder volumeName,
        int bufferLength);
}
