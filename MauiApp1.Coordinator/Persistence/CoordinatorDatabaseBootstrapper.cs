using Microsoft.Data.Sqlite;

namespace MauiApp1.Coordinator.Persistence;

public static class CoordinatorDatabaseBootstrapper
{
    public static async Task<string> BootstrapAsync(
        string windowsDatabasePath,
        string dataDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowsDatabasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        var sourcePath = Path.GetFullPath(windowsDatabasePath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("No se encontró la base local de Windows indicada.", sourcePath);
        }

        Directory.CreateDirectory(dataDirectory);
        var centralPath = Path.Combine(dataDirectory, "miscelanea-ari-central.db");
        if (File.Exists(centralPath))
        {
            throw new InvalidOperationException("La base central ya existe; no se sobrescribió.");
        }

        var backupDirectory = Path.Combine(dataDirectory, "Backups");
        Directory.CreateDirectory(backupDirectory);
        var backupPath = Path.Combine(backupDirectory,
            $"Windows-inicial-{DateTime.UtcNow:yyyyMMdd-HHmmss}.db");

        var sourceConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = sourcePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();

        try
        {
            await using var source = new SqliteConnection(sourceConnectionString);
            await source.OpenAsync(cancellationToken);
            await using (var integrityCommand = source.CreateCommand())
            {
                integrityCommand.CommandText = "PRAGMA integrity_check;";
                var integrityResult = await integrityCommand.ExecuteScalarAsync(cancellationToken);
                if (!string.Equals(integrityResult as string, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("La base de Windows no pasó la comprobación de integridad SQLite.");
                }
            }

            await using var central = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = centralPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString());
            await central.OpenAsync(cancellationToken);

            await using var backup = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = backupPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString());
            await backup.OpenAsync(cancellationToken);

            source.BackupDatabase(backup);
            source.BackupDatabase(central);
            return centralPath;
        }
        catch
        {
            if (File.Exists(centralPath)) File.Delete(centralPath);
            if (File.Exists(backupPath)) File.Delete(backupPath);
            throw;
        }
    }
}
