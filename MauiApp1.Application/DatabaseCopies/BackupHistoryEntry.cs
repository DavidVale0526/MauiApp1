namespace MauiApp1.Application.Backups;

public sealed record BackupHistoryEntry(
    Guid? BackupId,
    string FileName,
    DateTimeOffset? CreatedAtUtc,
    long SizeBytes,
    string Status,
    string? SchemaVersion,
    string? ApplicationVersion,
    string? Detail)
{
    public string VersionDescription =>
        $"Aplicación {ApplicationVersion ?? "desconocida"} · esquema {SchemaVersion ?? "desconocido"}";
}
