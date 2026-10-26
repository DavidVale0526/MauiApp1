using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MauiApp1.Application.Backups;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace MauiApp1.Infrastructure.Backups;

/// <summary>Creates a consistent, verified SQLite snapshot on a mounted removable volume.</summary>
public sealed class SqliteCentralDatabaseBackupService(
    IBackupDestinationProvider destinationProvider,
    ILogger<SqliteCentralDatabaseBackupService> logger) : ICentralDatabaseBackupService
{
    private const long MinimumFreeSpaceReserve = 16 * 1024 * 1024;
    private const int BackupFormatVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<IReadOnlyList<BackupHistoryEntry>> GetHistoryAsync(
        BackupDestination destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var mounted = await RequireMountedVolumeAsync(destination.VolumeId, cancellationToken).ConfigureAwait(false);
        var root = NormalizeRoot(mounted.MountPath);
        var directory = Path.GetFullPath(Path.Combine(root, "Miscelanea Ari", "Respaldos"));
        EnsureInsideVolume(root, directory);
        if (!Directory.Exists(directory))
            return Array.Empty<BackupHistoryEntry>();

        var entries = new List<BackupHistoryEntry>();
        foreach (var databasePath in Directory.EnumerateFiles(directory, "MiscelaneaAri-*.db", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = new FileInfo(databasePath);
            var metadataPath = databasePath + ".json";
            if (!File.Exists(metadataPath))
            {
                entries.Add(new BackupHistoryEntry(null, file.Name, file.LastWriteTimeUtc,
                    file.Length, "Revisar", null, null, "No se encontró la ficha de verificación."));
                continue;
            }

            try
            {
                await using var metadataStream = new FileStream(metadataPath, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                var manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(metadataStream, JsonOptions,
                    cancellationToken).ConfigureAwait(false);
                if (manifest is null || manifest.FormatVersion != BackupFormatVersion ||
                    !string.Equals(manifest.DatabaseFileName, file.Name, StringComparison.OrdinalIgnoreCase) ||
                    manifest.SizeBytes != file.Length || !IsSha256(manifest.Sha256))
                {
                    entries.Add(new BackupHistoryEntry(manifest?.BackupId, file.Name,
                        manifest?.CreatedAtUtc, file.Length, "Revisar", manifest?.SchemaVersion,
                        manifest?.ApplicationVersion, "La ficha no coincide con el archivo."));
                    continue;
                }

                entries.Add(new BackupHistoryEntry(manifest.BackupId, file.Name, manifest.CreatedAtUtc,
                    file.Length, "Creado y verificado", manifest.SchemaVersion,
                    manifest.ApplicationVersion, manifest.RetentionGroup == BackupRetentionGroup.Manual
                        ? "La integridad se comprobó al crear este respaldo."
                        : $"Respaldo automático ({GetRetentionGroupName(manifest.RetentionGroup)}); la integridad se comprobó al crearlo."));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                                JsonException or System.Security.SecurityException)
            {
                entries.Add(new BackupHistoryEntry(null, file.Name, file.LastWriteTimeUtc,
                    file.Length, "Revisar", null, null, "No se pudo leer la ficha de este respaldo."));
            }
        }

        foreach (var partialPath in Directory.EnumerateFiles(directory, "MiscelaneaAri-*.db.partial", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var partial = new FileInfo(partialPath);
            entries.Add(new BackupHistoryEntry(null, partial.Name, partial.LastWriteTimeUtc,
                partial.Length, "Incompleto", null, null,
                "La creación se interrumpió. El archivo no cuenta como respaldo válido."));
        }

        return entries.OrderByDescending(entry => entry.CreatedAtUtc).ToArray();
    }

    public async Task<CentralDatabaseBackup> CreateAsync(
        string centralDatabasePath,
        BackupDestination destination,
        CancellationToken cancellationToken = default,
        BackupRetentionGroup retentionGroup = BackupRetentionGroup.Manual)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(centralDatabasePath);
        ArgumentNullException.ThrowIfNull(destination);

        var sourcePath = Path.GetFullPath(centralDatabasePath);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("No se encontró la base central para respaldar.", sourcePath);

        var resolvedDestination = await RequireMountedVolumeAsync(destination.VolumeId, cancellationToken)
            .ConfigureAwait(false);
        var mountPath = NormalizeRoot(resolvedDestination.MountPath);
        if (!Path.IsPathRooted(mountPath) || !Directory.Exists(mountPath))
            throw new IOException("La memoria USB seleccionada ya no está disponible.");

        var backupDirectory = Path.GetFullPath(Path.Combine(mountPath, "Miscelanea Ari", "Respaldos"));
        EnsureInsideVolume(mountPath, backupDirectory);
        Directory.CreateDirectory(backupDirectory);

        var backupId = Guid.NewGuid();
        var createdAtUtc = DateTimeOffset.UtcNow;
        var createdAtLocal = createdAtUtc.ToLocalTime();
        var fileStem = $"MiscelaneaAri-{createdAtUtc:yyyyMMdd-HHmmss}-{backupId:N}";
        var finalPath = Path.Combine(backupDirectory, fileStem + ".db");
        var temporaryPath = finalPath + ".partial";
        var metadataPath = finalPath + ".json";
        var temporaryMetadataPath = metadataPath + ".partial";
        var lockPath = Path.Combine(backupDirectory, ".backup-operation.lock");

        await using var operationLock = AcquireOperationLock(lockPath);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceInfo = await GetSourceSnapshotInfoAsync(sourcePath, cancellationToken).ConfigureAwait(false);
            var latestDestination = await RequireMountedVolumeAsync(destination.VolumeId, cancellationToken)
                .ConfigureAwait(false);
            if (latestDestination.AvailableBytes < GetRequiredFreeSpace(sourceInfo.EstimatedBytes))
                throw new IOException("La memoria USB no tiene espacio suficiente para crear el respaldo.");

            await CreateSnapshotAsync(sourcePath, temporaryPath, cancellationToken).ConfigureAwait(false);
            var validatedSchemaVersion = await ValidateSnapshotAsync(temporaryPath, cancellationToken)
                .ConfigureAwait(false);
            var fileInfo = new FileInfo(temporaryPath);
            var hash = await ComputeSha256Async(temporaryPath, cancellationToken).ConfigureAwait(false);
            var applicationVersion = typeof(SqliteCentralDatabaseBackupService).Assembly
                                         .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                                     ?? typeof(SqliteCentralDatabaseBackupService).Assembly.GetName().Version?.ToString()
                                     ?? "desconocida";

            var result = new CentralDatabaseBackup(backupId, finalPath, metadataPath, createdAtUtc,
                createdAtLocal, fileInfo.Length, hash, validatedSchemaVersion, applicationVersion);
            var manifest = new BackupManifest(BackupFormatVersion, result.BackupId, result.CreatedAtUtc,
                result.CreatedAtLocal, Path.GetFileName(result.DatabasePath), result.SizeBytes,
                result.Sha256, result.SchemaVersion, result.ApplicationVersion, retentionGroup);

            await File.WriteAllTextAsync(temporaryMetadataPath,
                JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken).ConfigureAwait(false);
            await RequireSameMountedVolumeAsync(destination.VolumeId, mountPath, cancellationToken)
                .ConfigureAwait(false);

            File.Move(temporaryPath, finalPath);
            try
            {
                File.Move(temporaryMetadataPath, metadataPath);
            }
            catch
            {
                // Keep the verified database snapshot if writing its manifest fails.
                // It can be independently checked and must not be discarded as a valid copy.
                throw new IOException($"El respaldo se creó y verificó, pero no se pudo guardar su ficha: {metadataPath}");
            }

            await RequireSameMountedVolumeAsync(destination.VolumeId, mountPath, cancellationToken)
                .ConfigureAwait(false);
            logger.LogInformation("Central database backup {BackupId} completed and verified on removable volume {VolumeId} ({SizeBytes} bytes).",
                result.BackupId, destination.VolumeId, result.SizeBytes);
            return result;
        }
        catch (Exception exception)
        {
            TryDelete(temporaryPath);
            TryDelete(temporaryMetadataPath);
            logger.LogWarning(exception, "Central database backup failed for removable volume {VolumeId}.", destination.VolumeId);
            throw;
        }
    }

    public async Task ApplyRetentionAsync(
        BackupDestination destination,
        BackupRetentionGroup retentionGroup,
        int maximumBackups,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (retentionGroup == BackupRetentionGroup.Manual || !Enum.IsDefined(retentionGroup))
            throw new ArgumentOutOfRangeException(nameof(retentionGroup), "La retención solo aplica a respaldos programados.");
        if (maximumBackups < 1)
            throw new ArgumentOutOfRangeException(nameof(maximumBackups));

        var mounted = await RequireMountedVolumeAsync(destination.VolumeId, cancellationToken).ConfigureAwait(false);
        var root = NormalizeRoot(mounted.MountPath);
        var directory = Path.GetFullPath(Path.Combine(root, "Miscelanea Ari", "Respaldos"));
        EnsureInsideVolume(root, directory);
        if (!Directory.Exists(directory)) return;

        await using var operationLock = AcquireOperationLock(Path.Combine(directory, ".backup-operation.lock"));
        var matching = new List<(string DatabasePath, DateTimeOffset CreatedAtUtc)>();
        foreach (var databasePath in Directory.EnumerateFiles(directory, "MiscelaneaAri-*.db", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadataPath = databasePath + ".json";
            if (!File.Exists(metadataPath)) continue;
            try
            {
                await using var stream = new FileStream(metadataPath, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                var manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(stream, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                var info = new FileInfo(databasePath);
                if (manifest is null || manifest.RetentionGroup != retentionGroup ||
                    manifest.FormatVersion != BackupFormatVersion ||
                    !string.Equals(manifest.DatabaseFileName, info.Name, StringComparison.OrdinalIgnoreCase) ||
                    manifest.SizeBytes != info.Length || !IsSha256(manifest.Sha256)) continue;

                var actualHash = await ComputeSha256Async(databasePath, cancellationToken).ConfigureAwait(false);
                if (string.Equals(actualHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                    matching.Add((databasePath, manifest.CreatedAtUtc));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or
                                                System.Security.SecurityException)
            {
                logger.LogWarning(exception, "Skipping an unreadable backup during retention on volume {VolumeId}.", destination.VolumeId);
            }
        }

        foreach (var oldBackup in matching.OrderByDescending(item => item.CreatedAtUtc).Skip(maximumBackups))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = await RequireMountedVolumeAsync(destination.VolumeId, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(NormalizeRoot(current.MountPath), root, StringComparison.OrdinalIgnoreCase))
                throw new IOException("La memoria USB cambió de punto de montaje durante la retención; no se borraron más copias.");
            File.Delete(oldBackup.DatabasePath);
            File.Delete(oldBackup.DatabasePath + ".json");
        }
    }

    public async Task<BackupValidationResult> ValidateAsync(
        string currentCentralDatabasePath,
        BackupDestination destination,
        string backupFileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentCentralDatabasePath);
        ArgumentNullException.ThrowIfNull(destination);
        if (string.IsNullOrWhiteSpace(backupFileName) ||
            !string.Equals(Path.GetFileName(backupFileName), backupFileName, StringComparison.Ordinal) ||
            !backupFileName.StartsWith("MiscelaneaAri-", StringComparison.OrdinalIgnoreCase) ||
            !backupFileName.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Selecciona un archivo de respaldo de Miscelánea Ari válido.", nameof(backupFileName));

        var mounted = await RequireMountedVolumeAsync(destination.VolumeId, cancellationToken).ConfigureAwait(false);
        var volumeRoot = NormalizeRoot(mounted.MountPath);
        var backupDirectory = Path.GetFullPath(Path.Combine(volumeRoot, "Miscelanea Ari", "Respaldos"));
        EnsureInsideVolume(volumeRoot, backupDirectory);
        var databasePath = Path.GetFullPath(Path.Combine(backupDirectory, backupFileName));
        EnsureInsideVolume(backupDirectory, databasePath);
        if (!File.Exists(databasePath) || !File.Exists(databasePath + ".json"))
            throw new FileNotFoundException("El archivo o su ficha de respaldo ya no están en la memoria USB.", databasePath);

        BackupManifest? manifest;
        await using (var stream = new FileStream(databasePath + ".json", FileMode.Open, FileAccess.Read,
                         FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }

        var databaseFile = new FileInfo(databasePath);
        if (manifest is null || manifest.FormatVersion != BackupFormatVersion ||
            !string.Equals(manifest.DatabaseFileName, databaseFile.Name, StringComparison.OrdinalIgnoreCase) ||
            manifest.SizeBytes != databaseFile.Length || !IsSha256(manifest.Sha256))
            return new BackupValidationResult(false, false, backupFileName, manifest?.CreatedAtUtc,
                databaseFile.Length, manifest?.SchemaVersion, null,
                "La ficha no coincide con el archivo. No se debe restaurar esta copia.");

        var actualHash = await ComputeSha256Async(databasePath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(actualHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            return new BackupValidationResult(false, false, backupFileName, manifest.CreatedAtUtc,
                databaseFile.Length, manifest.SchemaVersion, null,
                "La suma de verificación no coincide. El archivo pudo dañarse o modificarse.");

        string backupSchemaVersion;
        try
        {
            backupSchemaVersion = await ValidateSnapshotAsync(databasePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is SqliteException or InvalidDataException or IOException)
        {
            logger.LogWarning(exception, "Backup {BackupFileName} failed its non-destructive validation.", backupFileName);
            return new BackupValidationResult(false, false, backupFileName, manifest.CreatedAtUtc,
                databaseFile.Length, manifest.SchemaVersion, null,
                "La copia no superó la comprobación de integridad o le faltan tablas necesarias.");
        }

        var currentSchemaVersion = await ReadLatestMigrationIdAsync(currentCentralDatabasePath, cancellationToken)
            .ConfigureAwait(false);
        var schemaMatchesManifest = string.Equals(backupSchemaVersion, manifest.SchemaVersion, StringComparison.Ordinal);
        var schemaCompatible = schemaMatchesManifest &&
                               string.Equals(backupSchemaVersion, currentSchemaVersion, StringComparison.Ordinal);
        var message = !schemaMatchesManifest
            ? "La versión de esquema de la ficha no coincide con la base validada. No se debe restaurar esta copia."
            : schemaCompatible
                ? "Copia íntegra y compatible con el esquema actual. La validación no modificó la base."
                : "La copia está íntegra, pero su esquema no coincide con el actual. No se habilita la restauración automática.";
        return new BackupValidationResult(true, schemaCompatible, backupFileName, manifest.CreatedAtUtc,
            databaseFile.Length, backupSchemaVersion, currentSchemaVersion, message);
    }

    private async Task<BackupDestination> RequireMountedVolumeAsync(
        string volumeId,
        CancellationToken cancellationToken)
    {
        var destination = await destinationProvider.ResolveAsync(volumeId, cancellationToken).ConfigureAwait(false);
        return destination ?? throw new IOException("La memoria USB seleccionada no está conectada o dejó de estar disponible.");
    }

    private async Task RequireSameMountedVolumeAsync(
        string volumeId,
        string originalMountPath,
        CancellationToken cancellationToken)
    {
        var current = await RequireMountedVolumeAsync(volumeId, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(NormalizeRoot(current.MountPath), originalMountPath, StringComparison.OrdinalIgnoreCase))
            throw new IOException("La memoria USB cambió de punto de montaje durante el respaldo. Vuelve a conectarla e inténtalo de nuevo.");
    }

    private static FileStream AcquireOperationLock(string lockPath)
    {
        try
        {
            return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                bufferSize: 1, FileOptions.DeleteOnClose);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException("Ya hay otro respaldo en curso en esta memoria USB.", exception);
        }
    }

    private static async Task<SourceSnapshotInfo> GetSourceSnapshotInfoAsync(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        await using var source = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = sourcePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        await source.OpenAsync(cancellationToken).ConfigureAwait(false);

        var pageCount = await ReadPragmaInt64Async(source, "PRAGMA page_count;", cancellationToken).ConfigureAwait(false);
        var pageSize = await ReadPragmaInt64Async(source, "PRAGMA page_size;", cancellationToken).ConfigureAwait(false);
        if (pageCount <= 0 || pageSize <= 0)
            throw new InvalidDataException("La base central no tiene un tamaño válido para respaldar.");

        var estimatedBytes = checked(pageCount * pageSize);
        return new SourceSnapshotInfo(estimatedBytes);
    }

    private static long GetRequiredFreeSpace(long estimatedBytes)
    {
        var safetyMargin = Math.Max(MinimumFreeSpaceReserve, estimatedBytes / 20);
        return checked(estimatedBytes + safetyMargin);
    }

    private static bool IsSha256(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static string GetRetentionGroupName(BackupRetentionGroup group) => group switch
    {
        BackupRetentionGroup.Daily => "diario",
        BackupRetentionGroup.Weekly => "semanal",
        BackupRetentionGroup.Monthly => "mensual",
        _ => "manual"
    };

    private static async Task CreateSnapshotAsync(
        string sourcePath,
        string targetPath,
        CancellationToken cancellationToken)
    {
        await using var source = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = sourcePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        await source.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var target = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = targetPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString());
        await target.OpenAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Run(() => source.BackupDatabase(target), cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ValidateSnapshotAsync(
        string snapshotPath,
        CancellationToken cancellationToken)
    {
        await using var snapshot = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = snapshotPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        await snapshot.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using (var integrity = snapshot.CreateCommand())
        {
            integrity.CommandText = "PRAGMA integrity_check;";
            var integrityResult = await integrity.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (!string.Equals(integrityResult as string, "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("El respaldo no pasó la comprobación de integridad de SQLite.");
        }

        foreach (var requiredTable in new[] { "DeviceIdentity", "Products", "UserAccounts", "Sales" })
        {
            await using var tableCheck = snapshot.CreateCommand();
            tableCheck.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1;";
            tableCheck.Parameters.AddWithValue("$name", requiredTable);
            if (await tableCheck.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is null)
                throw new InvalidDataException($"El respaldo no contiene la tabla necesaria «{requiredTable}».");
        }

        await using var migration = snapshot.CreateCommand();
        migration.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC LIMIT 1;";
        var schemaVersion = await migration.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        if (string.IsNullOrWhiteSpace(schemaVersion))
            throw new InvalidDataException("El respaldo no contiene el historial de versiones de la base de datos.");

        return schemaVersion;
    }

    private static async Task<string> ReadLatestMigrationIdAsync(string databasePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(databasePath))
            throw new FileNotFoundException("No se encontró la base central actual para comparar el esquema.", databasePath);

        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC LIMIT 1;";
        var migration = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        return string.IsNullOrWhiteSpace(migration)
            ? throw new InvalidDataException("La base central actual no contiene un esquema reconocible.")
            : migration;
    }

    private static async Task<long> ReadPragmaInt64Async(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static string NormalizeRoot(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return Path.EndsInDirectorySeparator(fullPath) ? fullPath : fullPath + Path.DirectorySeparatorChar;
    }

    private static void EnsureInsideVolume(string volumeRoot, string path)
    {
        var normalizedRoot = NormalizeRoot(volumeRoot);
        var normalizedPath = NormalizeRoot(path);
        if (!normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La carpeta de respaldo debe estar dentro de la memoria USB seleccionada.");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record SourceSnapshotInfo(long EstimatedBytes);

    private sealed record BackupManifest(
        int FormatVersion,
        Guid BackupId,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset CreatedAtLocal,
        string DatabaseFileName,
        long SizeBytes,
        string Sha256,
        string SchemaVersion,
        string ApplicationVersion,
        BackupRetentionGroup RetentionGroup = BackupRetentionGroup.Manual);
}
