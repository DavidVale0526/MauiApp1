using System.Security.Cryptography;
using System.Text.Json;
using MauiApp1.Application.Backups;
using MauiApp1.Infrastructure.Backups;
using Microsoft.Data.Sqlite;
using System.ServiceProcess;

namespace MauiApp1.Coordinator.Persistence;

/// <summary>
/// Performs the file replacement portion of a central restore. The caller must
/// stop every process that can write to the central database before invoking it.
/// </summary>
internal static class CoordinatorDatabaseRestoreService
{
    private const int BackupFormatVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task<string> RestoreAsync(
        string dataDirectory,
        string backupPath,
        bool requireManifest,
        CancellationToken cancellationToken = default,
        Action<string>? emergencyCreated = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);

        dataDirectory = Path.GetFullPath(dataDirectory);
        backupPath = Path.GetFullPath(backupPath);
        var centralPath = Path.Combine(dataDirectory, "miscelanea-ari-central.db");
        if (!File.Exists(centralPath))
            throw new FileNotFoundException("No se encontró la base central actual.", centralPath);
        if (!File.Exists(backupPath))
            throw new FileNotFoundException("No se encontró el archivo de respaldo seleccionado.", backupPath);
        if (string.Equals(centralPath, backupPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("El archivo seleccionado es la base central activa.");

        EnsureCoordinatorServiceStopped();
        if (File.Exists(centralPath + "-wal") || File.Exists(centralPath + "-shm"))
            throw new IOException("La base central aún tiene archivos SQLite de uso activo. No se reemplazó; verifica que la aplicación Host y el servicio estén cerrados e inténtalo de nuevo.");

        var currentDatabase = await ValidateDatabaseAsync(centralPath, cancellationToken).ConfigureAwait(false);
        var encryptedBackupPath = backupPath.EndsWith(".db.enc", StringComparison.OrdinalIgnoreCase);
        var decryptedBackupPath = encryptedBackupPath
            ? Path.Combine(dataDirectory, $"miscelanea-ari-central.restore-source-{Guid.NewGuid():N}.tmp")
            : null;
        var readableBackupPath = backupPath;
        DatabaseDescriptor backupDatabase;
        if (encryptedBackupPath)
        {
            if (!requireManifest)
                throw new InvalidOperationException("No se admite restaurar una copia cifrada sin su ficha de autenticación.");
            backupDatabase = await DecryptAndValidateBackupAsync(backupPath, decryptedBackupPath!, dataDirectory, cancellationToken)
                .ConfigureAwait(false);
            readableBackupPath = decryptedBackupPath!;
        }
        else
        {
            if (requireManifest)
                await ValidateManifestAsync(backupPath, cancellationToken).ConfigureAwait(false);
            backupDatabase = await ValidateDatabaseAsync(backupPath, cancellationToken).ConfigureAwait(false);
        }
        if (!string.Equals(currentDatabase.SchemaVersion, backupDatabase.SchemaVersion, StringComparison.Ordinal))
        {
            if (decryptedBackupPath is not null) TryDelete(decryptedBackupPath);
            throw new InvalidDataException(
                $"El esquema del respaldo ({backupDatabase.SchemaVersion}) no coincide con el esquema central ({currentDatabase.SchemaVersion}).");
        }
        if (currentDatabase.CoordinatorId != backupDatabase.CoordinatorId)
        {
            if (decryptedBackupPath is not null) TryDelete(decryptedBackupPath);
            throw new InvalidDataException("La copia pertenece a otro coordinador; no se restauró en este negocio.");
        }

        Directory.CreateDirectory(dataDirectory);
        var stamp = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        var emergencyDirectory = Path.Combine(dataDirectory, "Backups", $"PreRestore-{stamp}");
        Directory.CreateDirectory(emergencyDirectory);
        var emergencyPath = Path.Combine(emergencyDirectory, "miscelanea-ari-central-emergency.db");
        var stagedPath = Path.Combine(dataDirectory, $"miscelanea-ari-central.restore-{Guid.NewGuid():N}.tmp");
        var displacedPath = Path.Combine(emergencyDirectory, "miscelanea-ari-central-before-replace.db");
        var promoted = false;

        try
        {
            await CreateSqliteSnapshotAsync(centralPath, emergencyPath, cancellationToken).ConfigureAwait(false);
            var emergencyDatabase = await ValidateDatabaseAsync(emergencyPath, cancellationToken).ConfigureAwait(false);
            if (currentDatabase != emergencyDatabase)
                throw new InvalidDataException("La copia de emergencia no coincide con la base central actual.");
            emergencyCreated?.Invoke(emergencyPath);

            await CreateSqliteSnapshotAsync(readableBackupPath, stagedPath, cancellationToken).ConfigureAwait(false);
            var stagedDatabase = await ValidateDatabaseAsync(stagedPath, cancellationToken).ConfigureAwait(false);
            if (backupDatabase != stagedDatabase)
                throw new InvalidDataException("La copia temporal no coincide con el respaldo validado.");

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(centralPath, displacedPath);
            try
            {
                File.Move(stagedPath, centralPath);
                promoted = true;
            }
            catch
            {
                File.Move(displacedPath, centralPath);
                throw;
            }

            await ValidateDatabaseAsync(centralPath, cancellationToken).ConfigureAwait(false);
            return emergencyPath;
        }
        catch
        {
            if (promoted && File.Exists(emergencyPath))
            {
                var failedRestorePath = Path.Combine(emergencyDirectory, "miscelanea-ari-central-failed-restore.db");
                try
                {
                    if (File.Exists(centralPath)) File.Move(centralPath, failedRestorePath, overwrite: true);
                    await CreateSqliteSnapshotAsync(emergencyPath, stagedPath, CancellationToken.None).ConfigureAwait(false);
                    File.Move(stagedPath, centralPath, overwrite: true);
                    await ValidateDatabaseAsync(centralPath, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    if (!File.Exists(centralPath) && File.Exists(displacedPath))
                        File.Move(displacedPath, centralPath);
                    throw;
                }
            }
            else if (!File.Exists(centralPath) && File.Exists(displacedPath))
            {
                File.Move(displacedPath, centralPath);
            }

            throw;
        }
        finally
        {
            TryDelete(stagedPath);
            if (decryptedBackupPath is not null) TryDelete(decryptedBackupPath);
        }
    }

    private static async Task<DatabaseDescriptor> DecryptAndValidateBackupAsync(
        string encryptedPath, string plaintextPath, string dataDirectory, CancellationToken cancellationToken)
    {
        var manifestPath = encryptedPath + ".json";
        if (!File.Exists(manifestPath))
            throw new InvalidDataException("La copia cifrada no tiene su ficha de verificación.");
        BackupManifest? manifest;
        await using (var stream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                         4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        var encryptedInfo = new FileInfo(encryptedPath);
        if (manifest?.EncryptionEnvelope is null || manifest.FormatVersion != BackupFormatVersion ||
            !string.Equals(manifest.DatabaseFileName, encryptedInfo.Name, StringComparison.OrdinalIgnoreCase) ||
            manifest.SizeBytes != encryptedInfo.Length || manifest.Sha256 is not { Length: 64 } ||
            manifest.Sha256.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("La ficha de la copia cifrada no coincide con el archivo.");

        await using (var encryptedStream = new FileStream(encryptedPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                         1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            var hash = await SHA256.HashDataAsync(encryptedStream, cancellationToken).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(hash, Convert.FromHexString(manifest.Sha256)))
                throw new InvalidDataException("El hash del respaldo cifrado no coincide con su ficha.");
        }

        var keyStore = new WindowsCentralBackupEncryptionKeyStore(dataDirectory);
        var material = await keyStore.ReadAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new CryptographicException("La clave local de respaldos no está disponible. Desbloquea la copia desde la aplicación antes de restaurarla.");
        try
        {
            var payload = new BackupManifestAuthenticationPayload(manifest.FormatVersion, manifest.BackupId,
                manifest.CreatedAtUtc, manifest.CreatedAtLocal, manifest.DatabaseFileName, manifest.SizeBytes,
                manifest.Sha256, manifest.SchemaVersion, manifest.ApplicationVersion, manifest.RetentionGroup,
                manifest.EncryptionEnvelope, manifest.PlaintextSizeBytes ?? throw new InvalidDataException("La ficha no incluye el tamaño original de la base."));
            if (!BackupEncryptionCryptography.VerifyManifestMac(material.DataKey, payload, manifest.MetadataMacBase64))
                throw new CryptographicException("La ficha autenticada del respaldo fue modificada o está dañada.");
            await AuthenticatedBackupFileCryptography.DecryptAsync(encryptedPath, plaintextPath, material.DataKey, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material.DataKey);
        }

        try
        {
            var database = await ValidateDatabaseAsync(plaintextPath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(database.SchemaVersion, manifest.SchemaVersion, StringComparison.Ordinal))
                throw new InvalidDataException("La versión de esquema no coincide con la ficha de respaldo.");
            return database;
        }
        catch
        {
            TryDelete(plaintextPath);
            throw;
        }
    }

    private static async Task ValidateManifestAsync(string databasePath, CancellationToken cancellationToken)
    {
        var manifestPath = databasePath + ".json";
        if (!File.Exists(manifestPath))
            throw new InvalidDataException("El respaldo no tiene su ficha de verificación.");

        BackupManifest? manifest;
        await using (var stream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                         4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }

        var info = new FileInfo(databasePath);
        if (manifest is null || manifest.FormatVersion != BackupFormatVersion ||
            !string.Equals(manifest.DatabaseFileName, info.Name, StringComparison.OrdinalIgnoreCase) ||
            manifest.SizeBytes != info.Length || manifest.Sha256 is not { Length: 64 } ||
            manifest.Sha256.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("La ficha de respaldo no coincide con el archivo.");

        await using var file = new FileStream(databasePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false));
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actualHash), Convert.FromHexString(manifest.Sha256)))
            throw new InvalidDataException("El hash del respaldo no coincide con su ficha.");

        var database = await ValidateDatabaseAsync(databasePath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(database.SchemaVersion, manifest.SchemaVersion, StringComparison.Ordinal))
            throw new InvalidDataException("La versión de esquema no coincide con la ficha de respaldo.");
    }

    private static async Task<DatabaseDescriptor> ValidateDatabaseAsync(string path, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using (var integrity = connection.CreateCommand())
        {
            integrity.CommandText = "PRAGMA integrity_check;";
            var result = await integrity.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"La base «{Path.GetFileName(path)}» no pasó la comprobación de integridad SQLite.");
        }

        foreach (var table in new[] { "DeviceIdentity", "Products", "UserAccounts", "Sales" })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name LIMIT 1;";
            command.Parameters.AddWithValue("$name", table);
            if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is null)
                throw new InvalidDataException($"La base no contiene la tabla requerida «{table}».");
        }

        await using var identity = connection.CreateCommand();
        identity.CommandText = "SELECT DeviceId FROM DeviceIdentity WHERE SingletonKey = 1 LIMIT 1;";
        var rawCoordinatorId = await identity.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        if (!Guid.TryParse(rawCoordinatorId, out var coordinatorId) || coordinatorId == Guid.Empty)
            throw new InvalidDataException("La base no contiene una identidad válida del coordinador.");

        await using var migration = connection.CreateCommand();
        migration.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC LIMIT 1;";
        var schema = await migration.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        return string.IsNullOrWhiteSpace(schema)
            ? throw new InvalidDataException("La base no contiene un esquema reconocido.")
            : new DatabaseDescriptor(schema, coordinatorId);
    }

    private static async Task CreateSqliteSnapshotAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        TryDelete(destinationPath);
        await using var source = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = sourcePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        await source.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = destinationPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString());
        await destination.OpenAsync(cancellationToken).ConfigureAwait(false);
        await Task.Run(() => source.BackupDatabase(destination), cancellationToken).ConfigureAwait(false);
    }

    private static void EnsureCoordinatorServiceStopped()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("La restauración de la base central solo se admite en Windows Host.");

        try
        {
            using var service = new ServiceController("MiscelaneaAriCoordinator");
            service.Refresh();
            if (service.Status != ServiceControllerStatus.Stopped)
                throw new InvalidOperationException("Detén primero el servicio coordinador. La base central no se modificó.");
        }
        catch (InvalidOperationException exception) when
            (!exception.Message.Contains("Detén primero", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "No se pudo comprobar que el servicio coordinador esté detenido. La base central no se modificó.", exception);
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new InvalidOperationException(
                "Windows no permitió comprobar el estado del coordinador. La base central no se modificó.", exception);
        }
    }

    private static void TryDelete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

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
        BackupRetentionGroup RetentionGroup = BackupRetentionGroup.Manual,
        BackupEncryptionEnvelope? EncryptionEnvelope = null,
        long? PlaintextSizeBytes = null,
        string? MetadataMacBase64 = null);

    private sealed record DatabaseDescriptor(string SchemaVersion, Guid CoordinatorId);
}
