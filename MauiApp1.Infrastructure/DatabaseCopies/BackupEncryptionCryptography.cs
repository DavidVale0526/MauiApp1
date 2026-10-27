using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MauiApp1.Application.Backups;

namespace MauiApp1.Infrastructure.Backups;

/// <summary>Creates and opens the passphrase envelope that protects a random backup data key.</summary>
public static class BackupEncryptionCryptography
{
    public const int EnvelopeVersion = 1;
    public const int RecommendedPbkdf2Iterations = 600_000;
    public const int DataKeyLengthBytes = 32;
    private const int SaltLengthBytes = 16;
    private const int NonceLengthBytes = 12;
    private const int AuthenticationTagLengthBytes = 16;
    private const int MinimumPassphraseLength = 12;
    private static readonly byte[] AssociatedData = Encoding.UTF8.GetBytes("MiscelaneaAri.BackupDataKey.v1");

    /// <summary>Wraps a fresh random data key; the passphrase itself is never returned or persisted.</summary>
    public static BackupEncryptionEnvelope CreateEnvelope(
        ReadOnlySpan<byte> dataKey,
        string recoveryPassphrase)
    {
        ArgumentNullException.ThrowIfNull(recoveryPassphrase);
        if (dataKey.Length != DataKeyLengthBytes)
            throw new ArgumentException("La clave de datos debe tener 256 bits.", nameof(dataKey));
        if (recoveryPassphrase.Length < MinimumPassphraseLength)
            throw new ArgumentException("La frase de recuperación debe tener al menos 12 caracteres.", nameof(recoveryPassphrase));

        var salt = RandomNumberGenerator.GetBytes(SaltLengthBytes);
        var nonce = RandomNumberGenerator.GetBytes(NonceLengthBytes);
        var wrappingKey = DeriveWrappingKey(recoveryPassphrase, salt, RecommendedPbkdf2Iterations);
        var ciphertext = new byte[dataKey.Length];
        var tag = new byte[AuthenticationTagLengthBytes];
        try
        {
            using var aes = new AesGcm(wrappingKey, AuthenticationTagLengthBytes);
            aes.Encrypt(nonce, dataKey, ciphertext, tag, AssociatedData);
            return new BackupEncryptionEnvelope(
                EnvelopeVersion,
                RecommendedPbkdf2Iterations,
                Convert.ToBase64String(salt),
                Convert.ToBase64String(nonce),
                Convert.ToBase64String(ciphertext),
                Convert.ToBase64String(tag));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(tag);
        }
    }

    /// <summary>Opens a recovery envelope or throws CryptographicException for an incorrect phrase/tampered data.</summary>
    public static byte[] UnwrapDataKey(BackupEncryptionEnvelope envelope, string recoveryPassphrase)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(recoveryPassphrase);
        if (envelope.Version != EnvelopeVersion || envelope.KdfIterations < RecommendedPbkdf2Iterations ||
            envelope.KdfIterations > 5_000_000)
            throw new InvalidDataException("La ficha usa parámetros de recuperación no admitidos.");

        byte[] salt;
        byte[] nonce;
        byte[] ciphertext;
        byte[] tag;
        try
        {
            salt = Convert.FromBase64String(envelope.SaltBase64);
            nonce = Convert.FromBase64String(envelope.NonceBase64);
            ciphertext = Convert.FromBase64String(envelope.WrappedDataKeyBase64);
            tag = Convert.FromBase64String(envelope.AuthenticationTagBase64);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("La ficha de recuperación está dañada.", exception);
        }

        if (salt.Length != SaltLengthBytes || nonce.Length != NonceLengthBytes ||
            ciphertext.Length != DataKeyLengthBytes || tag.Length != AuthenticationTagLengthBytes)
            throw new InvalidDataException("La ficha de recuperación tiene longitudes no válidas.");

        var wrappingKey = DeriveWrappingKey(recoveryPassphrase, salt, envelope.KdfIterations);
        var dataKey = new byte[DataKeyLengthBytes];
        try
        {
            using var aes = new AesGcm(wrappingKey, AuthenticationTagLengthBytes);
            aes.Decrypt(nonce, ciphertext, tag, dataKey, AssociatedData);
            return dataKey;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(dataKey);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(tag);
        }
    }

    public static string ComputeManifestMac(byte[] dataKey, BackupManifestAuthenticationPayload payload)
    {
        ArgumentNullException.ThrowIfNull(dataKey);
        ArgumentNullException.ThrowIfNull(payload);
        if (dataKey.Length != DataKeyLengthBytes)
            throw new ArgumentException("La clave de datos debe tener 256 bits.", nameof(dataKey));
        var macKey = HMACSHA256.HashData(dataKey, Encoding.UTF8.GetBytes("MiscelaneaAri.BackupManifest.Authentication.v1"));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        try { return Convert.ToBase64String(HMACSHA256.HashData(macKey, bytes)); }
        finally
        {
            CryptographicOperations.ZeroMemory(macKey);
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public static bool VerifyManifestMac(byte[] dataKey, BackupManifestAuthenticationPayload payload, string? actualMac)
    {
        if (string.IsNullOrWhiteSpace(actualMac)) return false;
        byte[] supplied;
        try { supplied = Convert.FromBase64String(actualMac); }
        catch (FormatException) { return false; }
        var expected = Convert.FromBase64String(ComputeManifestMac(dataKey, payload));
        try { return supplied.Length == expected.Length && CryptographicOperations.FixedTimeEquals(supplied, expected); }
        finally
        {
            CryptographicOperations.ZeroMemory(supplied);
            CryptographicOperations.ZeroMemory(expected);
        }
    }

    private static byte[] DeriveWrappingKey(string passphrase, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, iterations, HashAlgorithmName.SHA256, DataKeyLengthBytes);
}

public sealed record BackupManifestAuthenticationPayload(
    int FormatVersion,
    Guid BackupId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset CreatedAtLocal,
    string DatabaseFileName,
    long SizeBytes,
    string Sha256,
    string SchemaVersion,
    string ApplicationVersion,
    MauiApp1.Application.Backups.BackupRetentionGroup RetentionGroup,
    BackupEncryptionEnvelope EncryptionEnvelope,
    long PlaintextSizeBytes);
