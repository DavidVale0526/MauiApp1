using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace MauiApp1.Coordinator.Pairing;

internal sealed class CoordinatorPairingStore(string databasePath)
{
    private static readonly TimeSpan PairingCodeLifetime = TimeSpan.FromMinutes(5);
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWrite,
        Pooling = false
    }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS PendingDevicePairings (
                PairingId TEXT NOT NULL PRIMARY KEY,
                Salt BLOB NOT NULL,
                CodeHash BLOB NOT NULL,
                CreatedAtUtc INTEGER NOT NULL,
                ExpiresAtUtc INTEGER NOT NULL,
                RedeemedAtUtc INTEGER NULL
            );
            CREATE TABLE IF NOT EXISTS AuthorizedDevices (
                DeviceId TEXT NOT NULL PRIMARY KEY,
                TokenHash BLOB NOT NULL,
                AuthorizedAtUtc INTEGER NOT NULL,
                IsRevoked INTEGER NOT NULL DEFAULT 0 CHECK (IsRevoked IN (0, 1))
            );
            CREATE TABLE IF NOT EXISTS CoordinatorUserSessions (
                TokenHash BLOB NOT NULL PRIMARY KEY,
                DeviceId TEXT NOT NULL,
                UserId TEXT NOT NULL,
                CreatedAtUtc INTEGER NOT NULL,
                ExpiresAtUtc INTEGER NOT NULL,
                RevokedAtUtc INTEGER NULL
            );
            CREATE INDEX IF NOT EXISTS IX_CoordinatorUserSessions_Device_Expiry
                ON CoordinatorUserSessions (DeviceId, ExpiresAtUtc);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<DateTime> CreateUserSessionAsync(
        Guid deviceId, Guid userId, string sessionToken, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var expires = now.AddHours(12);
        var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(sessionToken));
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var clean = connection.CreateCommand())
        {
            clean.Transaction = (SqliteTransaction)transaction;
            clean.CommandText = "DELETE FROM CoordinatorUserSessions WHERE ExpiresAtUtc <= $now OR RevokedAtUtc IS NOT NULL;";
            clean.Parameters.AddWithValue("$now", now.Ticks);
            await clean.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = (SqliteTransaction)transaction;
            insert.CommandText = "INSERT INTO CoordinatorUserSessions (TokenHash, DeviceId, UserId, CreatedAtUtc, ExpiresAtUtc) VALUES ($hash, $device, $user, $created, $expires);";
            insert.Parameters.AddWithValue("$hash", tokenHash);
            insert.Parameters.AddWithValue("$device", deviceId.ToString("D"));
            insert.Parameters.AddWithValue("$user", userId.ToString("D"));
            insert.Parameters.AddWithValue("$created", now.Ticks);
            insert.Parameters.AddWithValue("$expires", expires.Ticks);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return expires;
    }

    public async Task<Guid?> GetSessionUserIdAsync(
        Guid deviceId, string sessionToken, CancellationToken cancellationToken = default)
    {
        if (deviceId == Guid.Empty || string.IsNullOrWhiteSpace(sessionToken)) return null;
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(sessionToken));
        var now = DateTime.UtcNow.Ticks;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.TokenHash, s.UserId
            FROM CoordinatorUserSessions AS s
            INNER JOIN UserAccounts AS u ON u.UserId = s.UserId
            WHERE s.DeviceId = $device AND s.ExpiresAtUtc > $now AND s.RevokedAtUtc IS NULL AND u.IsEnabled = 1;
            """;
        command.Parameters.AddWithValue("$device", deviceId.ToString("D"));
        command.Parameters.AddWithValue("$now", now);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader[0] is byte[] expectedHash && CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash))
                return Guid.TryParse(reader.GetString(1), out var userId) ? userId : null;
        }
        return null;
    }

    public async Task<NewDevicePairing> CreateCodeAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var expires = now.Add(PairingCodeLifetime);
        var code = RandomNumberGenerator.GetInt32(10_000_000, 100_000_000).ToString();
        var salt = RandomNumberGenerator.GetBytes(16);
        var codeHash = HashCode(salt, code);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var revoke = connection.CreateCommand())
        {
            revoke.Transaction = (SqliteTransaction)transaction;
            revoke.CommandText = "UPDATE PendingDevicePairings SET RedeemedAtUtc = $now WHERE RedeemedAtUtc IS NULL;";
            revoke.Parameters.AddWithValue("$now", now.Ticks);
            await revoke.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = (SqliteTransaction)transaction;
            insert.CommandText = """
                INSERT INTO PendingDevicePairings
                    (PairingId, Salt, CodeHash, CreatedAtUtc, ExpiresAtUtc, RedeemedAtUtc)
                VALUES ($id, $salt, $hash, $created, $expires, NULL);
                """;
            insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
            insert.Parameters.AddWithValue("$salt", salt);
            insert.Parameters.AddWithValue("$hash", codeHash);
            insert.Parameters.AddWithValue("$created", now.Ticks);
            insert.Parameters.AddWithValue("$expires", expires.Ticks);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new NewDevicePairing(code, expires);
    }

    public async Task<bool> IsAuthorizedAsync(Guid deviceId, string deviceToken, CancellationToken cancellationToken = default)
    {
        if (deviceId == Guid.Empty || string.IsNullOrWhiteSpace(deviceToken)) return false;
        var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(deviceToken));
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TokenHash FROM AuthorizedDevices WHERE DeviceId = $deviceId AND IsRevoked = 0 LIMIT 1;";
        command.Parameters.AddWithValue("$deviceId", deviceId.ToString("D"));
        if (await command.ExecuteScalarAsync(cancellationToken) is not byte[] expectedHash) return false;
        return CryptographicOperations.FixedTimeEquals(expectedHash, tokenHash);
    }

    public async Task<DevicePairingResult> CompletePairingAsync(
        Guid deviceId,
        string code,
        string deviceToken,
        CancellationToken cancellationToken = default)
    {
        if (deviceId == Guid.Empty || code.Length != 8 || code.Any(character => character is < '0' or > '9') ||
            deviceToken.Length != 43 || deviceToken.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
        {
            return DevicePairingResult.Rejected;
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        // An immediate transaction serializes redemption and recovery of a lost response.
        using var transaction = connection.BeginTransaction(deferred: false);
        var now = DateTime.UtcNow;
        var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(deviceToken));
        await using (var existing = connection.CreateCommand())
        {
            existing.Transaction = transaction;
            existing.CommandText = "SELECT TokenHash FROM AuthorizedDevices WHERE DeviceId = $deviceId AND IsRevoked = 0 LIMIT 1;";
            existing.Parameters.AddWithValue("$deviceId", deviceId.ToString("D"));
            if (await existing.ExecuteScalarAsync(cancellationToken) is byte[] existingHash)
            {
                await transaction.RollbackAsync(cancellationToken);
                return CryptographicOperations.FixedTimeEquals(existingHash, tokenHash)
                    ? DevicePairingResult.Authorized
                    : DevicePairingResult.AlreadyAuthorized;
            }
        }
        byte[]? salt = null;
        byte[]? expectedHash = null;
        string? pairingId = null;

        await using (var find = connection.CreateCommand())
        {
            find.Transaction = transaction;
            find.CommandText = """
                SELECT PairingId, Salt, CodeHash
                FROM PendingDevicePairings
                WHERE RedeemedAtUtc IS NULL AND ExpiresAtUtc > $now
                ORDER BY CreatedAtUtc DESC LIMIT 1;
                """;
            find.Parameters.AddWithValue("$now", now.Ticks);
            await using var reader = await find.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                pairingId = reader.GetString(0);
                salt = (byte[])reader[1];
                expectedHash = (byte[])reader[2];
            }
        }

        if (pairingId is null || salt is null || expectedHash is null ||
            !CryptographicOperations.FixedTimeEquals(HashCode(salt, code), expectedHash))
        {
            await transaction.RollbackAsync(cancellationToken);
            return DevicePairingResult.Rejected;
        }

        await using (var redeem = connection.CreateCommand())
        {
            redeem.Transaction = transaction;
            redeem.CommandText = """
                UPDATE PendingDevicePairings SET RedeemedAtUtc = $now
                WHERE PairingId = $id AND RedeemedAtUtc IS NULL AND ExpiresAtUtc > $now;
                """;
            redeem.Parameters.AddWithValue("$now", now.Ticks);
            redeem.Parameters.AddWithValue("$id", pairingId);
            if (await redeem.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return DevicePairingResult.Rejected;
            }
        }

        await using (var authorize = connection.CreateCommand())
        {
            authorize.Transaction = transaction;
            authorize.CommandText = """
                INSERT INTO AuthorizedDevices (DeviceId, TokenHash, AuthorizedAtUtc, IsRevoked)
                VALUES ($deviceId, $tokenHash, $now, 0)
                ON CONFLICT(DeviceId) DO UPDATE SET
                    TokenHash = excluded.TokenHash,
                    AuthorizedAtUtc = excluded.AuthorizedAtUtc,
                    IsRevoked = 0;
                """;
            authorize.Parameters.AddWithValue("$deviceId", deviceId.ToString("D"));
            authorize.Parameters.AddWithValue("$tokenHash", tokenHash);
            authorize.Parameters.AddWithValue("$now", now.Ticks);
            await authorize.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return DevicePairingResult.Authorized;
    }

    private static byte[] HashCode(byte[] salt, string code)
    {
        var codeBytes = Encoding.ASCII.GetBytes(code);
        var input = new byte[salt.Length + codeBytes.Length];
        Buffer.BlockCopy(salt, 0, input, 0, salt.Length);
        Buffer.BlockCopy(codeBytes, 0, input, salt.Length, codeBytes.Length);
        return SHA256.HashData(input);
    }
}

internal sealed record NewDevicePairing(string Code, DateTime ExpiresAtUtc);

internal sealed record DevicePairingResult(bool IsAuthorized, bool IsAlreadyAuthorized)
{
    public static DevicePairingResult Rejected => new(false, false);
    public static DevicePairingResult AlreadyAuthorized => new(false, true);
    public static DevicePairingResult Authorized => new(true, false);
}
