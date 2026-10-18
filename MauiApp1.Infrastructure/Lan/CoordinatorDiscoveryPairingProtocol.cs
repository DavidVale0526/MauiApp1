using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace MauiApp1.Infrastructure.Lan;

public sealed class CoordinatorDiscoveryEncryptedPayload
{
    public string? Nonce { get; set; } = string.Empty;
    public string? Ciphertext { get; set; } = string.Empty;
    public string? Tag { get; set; } = string.Empty;
}

public static class CoordinatorDiscoveryPairingProtocol
{
    private static readonly byte[] ProtocolLabel = Encoding.ASCII.GetBytes("MiscelaneaAri.DiscoveryPairing.v1");

    public static byte[] CreateEphemeralKeyPair(out ECDiffieHellman keyPair, out byte[] nonce)
    {
        keyPair = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        nonce = RandomNumberGenerator.GetBytes(32);
        return keyPair.ExportSubjectPublicKeyInfo();
    }

    // The public-key order is always client first, server second, regardless of which side calls this method.
    public static byte[] DeriveSessionKey(
        ECDiffieHellman ownKey,
        byte[] peerPublicKey,
        byte[] clientPublicKey,
        byte[] serverPublicKey,
        Guid requestId,
        Guid deviceId,
        byte[] clientNonce,
        byte[] serverNonce,
        string coordinatorId)
    {
        if (clientPublicKey.Length is < 64 or > 1024 || serverPublicKey.Length is < 64 or > 1024 ||
            clientNonce.Length != 32 || serverNonce.Length != 32 || requestId == Guid.Empty ||
            deviceId == Guid.Empty || coordinatorId.Length != 32 || !coordinatorId.All(Uri.IsHexDigit))
            throw new CryptographicException("La negociación de vínculo no es válida.");

        using var peer = ECDiffieHellman.Create();
        peer.ImportSubjectPublicKeyInfo(peerPublicKey, out var bytesRead);
        if (bytesRead != peerPublicKey.Length) throw new CryptographicException("La clave pública del otro dispositivo no es válida.");
        var ownPublicKey = ownKey.ExportSubjectPublicKeyInfo();
        var ownIsClient = CryptographicOperations.FixedTimeEquals(ownPublicKey, clientPublicKey);
        var ownIsServer = CryptographicOperations.FixedTimeEquals(ownPublicKey, serverPublicKey);
        var peerMatchesTranscript = CryptographicOperations.FixedTimeEquals(peerPublicKey,
            ownIsClient ? serverPublicKey : clientPublicKey);
        if (ownIsClient == ownIsServer || !peerMatchesTranscript)
            throw new CryptographicException("Las claves no corresponden a la negociación de vínculo.");
        var sharedSecret = ownKey.DeriveKeyMaterial(peer.PublicKey);
        try
        {
            var transcript = BuildTranscript(requestId, deviceId, clientNonce, serverNonce,
                coordinatorId.ToUpperInvariant(), clientPublicKey, serverPublicKey);
            var transcriptHash = SHA256.HashData(transcript);
            return HkdfSha256(sharedSecret, transcriptHash, Concat(ProtocolLabel, Encoding.ASCII.GetBytes("/root")), 32);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sharedSecret);
        }
    }

    public static string ComputeVerificationCode(byte[] sessionKey)
    {
        var proof = HMACSHA256.HashData(sessionKey, Concat(ProtocolLabel, Encoding.ASCII.GetBytes("/compare")));
        var number = BinaryPrimitives.ReadUInt64BigEndian(proof.AsSpan(0, sizeof(ulong))) % 100_000_000UL;
        CryptographicOperations.ZeroMemory(proof);
        return number.ToString("D8", System.Globalization.CultureInfo.InvariantCulture);
    }

    public static CoordinatorDiscoveryEncryptedPayload Encrypt(
        byte[] sessionKey,
        Guid requestId,
        string purpose,
        byte[] plaintext)
    {
        var key = HkdfSha256(sessionKey, ProtocolLabel, Encoding.UTF8.GetBytes("MiscelaneaAri.DiscoveryPairing.v1/" + purpose), 32);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        var associatedData = Concat(ProtocolLabel, requestId.ToByteArray(), Encoding.UTF8.GetBytes("/" + purpose));
        try
        {
            using var aes = new AesGcm(key, tag.Length);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
            return new CoordinatorDiscoveryEncryptedPayload
            {
                Nonce = Convert.ToBase64String(nonce),
                Ciphertext = Convert.ToBase64String(ciphertext),
                Tag = Convert.ToBase64String(tag)
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public static byte[] Decrypt(
        byte[] sessionKey,
        Guid requestId,
        string purpose,
        CoordinatorDiscoveryEncryptedPayload payload)
    {
        if (payload is null || string.IsNullOrWhiteSpace(payload.Nonce) || payload.Nonce.Length > 64 ||
            string.IsNullOrWhiteSpace(payload.Ciphertext) || payload.Ciphertext.Length > 24_000 ||
            string.IsNullOrWhiteSpace(payload.Tag) || payload.Tag.Length > 64)
            throw new CryptographicException("El mensaje protegido no es válido.");
        byte[] nonce;
        byte[] ciphertext;
        byte[] tag;
        try
        {
            nonce = Convert.FromBase64String(payload.Nonce);
            ciphertext = Convert.FromBase64String(payload.Ciphertext);
            tag = Convert.FromBase64String(payload.Tag);
        }
        catch (FormatException exception)
        {
            throw new CryptographicException("El mensaje protegido no es válido.", exception);
        }
        if (nonce.Length != 12 || tag.Length != 16 || ciphertext.Length > 16_384)
            throw new CryptographicException("El mensaje protegido no es válido.");
        var key = HkdfSha256(sessionKey, ProtocolLabel, Encoding.UTF8.GetBytes("MiscelaneaAri.DiscoveryPairing.v1/" + purpose), 32);
        var plaintext = new byte[ciphertext.Length];
        var associatedData = Concat(ProtocolLabel, requestId.ToByteArray(), Encoding.UTF8.GetBytes("/" + purpose));
        try
        {
            using var aes = new AesGcm(key, tag.Length);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
            return plaintext;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static byte[] BuildTranscript(Guid requestId, Guid deviceId, byte[] clientNonce,
        byte[] serverNonce, string coordinatorId, byte[]? clientPublicKey, byte[]? serverPublicKey)
    {
        using var output = new MemoryStream();
        output.Write(ProtocolLabel);
        output.WriteByte(0);
        output.Write(requestId.ToByteArray());
        output.Write(deviceId.ToByteArray());
        WriteLengthPrefixed(output, clientPublicKey ?? []);
        WriteLengthPrefixed(output, serverPublicKey ?? []);
        output.Write(clientNonce);
        output.Write(serverNonce);
        output.Write(Encoding.ASCII.GetBytes(coordinatorId));
        return output.ToArray();
    }

    private static byte[] HkdfSha256(byte[] inputKeyMaterial, byte[] salt, byte[] info, int outputLength)
    {
        var pseudorandomKey = HMACSHA256.HashData(salt, inputKeyMaterial);
        try
        {
            var blockInput = new byte[info.Length + 1];
            info.CopyTo(blockInput, 0);
            blockInput[^1] = 1;
            var block = HMACSHA256.HashData(pseudorandomKey, blockInput);
            try { return block.AsSpan(0, outputLength).ToArray(); }
            finally { CryptographicOperations.ZeroMemory(block); }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pseudorandomKey);
        }
    }

    private static void WriteLengthPrefixed(Stream output, byte[] value)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, value.Length);
        output.Write(length);
        output.Write(value);
    }

    private static byte[] Concat(params byte[][] values)
    {
        var result = new byte[values.Sum(value => value.Length)];
        var offset = 0;
        foreach (var value in values)
        {
            value.CopyTo(result, offset);
            offset += value.Length;
        }
        return result;
    }

}
