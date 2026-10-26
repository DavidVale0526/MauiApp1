using System.Text.Json;
using MauiApp1.Application.Backups;

namespace MauiApp1.Infrastructure.Backups;

/// <summary>Shares backup settings and scheduler status between the Host UI and Windows service.</summary>
public sealed class JsonCoordinatorBackupScheduleStore : ICoordinatorBackupScheduleStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _settingsPath;
    private readonly string _statusPath;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public JsonCoordinatorBackupScheduleStore(string coordinatorDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(coordinatorDataDirectory);
        var dataDirectory = Path.GetFullPath(coordinatorDataDirectory);
        _settingsPath = Path.Combine(dataDirectory, "backup-schedule.json");
        _statusPath = Path.Combine(dataDirectory, "backup-schedule-status.json");
    }

    public Task<BackupScheduleSettings?> ReadSettingsAsync(CancellationToken cancellationToken = default) =>
        ReadAsync<BackupScheduleSettings>(_settingsPath, cancellationToken);

    public async Task SaveSettingsAsync(
        BackupScheduleSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!Enum.IsDefined(settings.Frequency) || !Enum.IsDefined(settings.WeeklyDay) ||
            settings.MonthlyDay is < 1 or > 28)
            throw new ArgumentException("La programación de respaldos contiene una frecuencia o fecha no válida.", nameof(settings));
        if (settings.IsEnabled && string.IsNullOrWhiteSpace(settings.TargetVolumeId))
            throw new ArgumentException("Selecciona una memoria USB antes de activar la programación.", nameof(settings));

        await WriteAsync(_settingsPath, settings, cancellationToken).ConfigureAwait(false);
    }

    public Task<BackupScheduleRunStatus?> ReadLastRunAsync(CancellationToken cancellationToken = default) =>
        ReadAsync<BackupScheduleRunStatus>(_statusPath, cancellationToken);

    public Task SaveLastRunAsync(
        BackupScheduleRunStatus status,
        CancellationToken cancellationToken = default) =>
        WriteAsync(_statusPath, status, cancellationToken);

    private static async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return default;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporaryPath = null;
        try
        {
            var directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, path, overwrite: true);
            temporaryPath = null;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try { File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            _writeGate.Release();
        }
    }
}
