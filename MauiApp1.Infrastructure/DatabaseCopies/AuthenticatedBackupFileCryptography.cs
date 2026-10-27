using System.Security.Cryptography;
using System.Text;

namespace MauiApp1.Infrastructure.Backups;

/// <summary>Streams an AES-256-CBC backup file and authenticates it with HMAC-SHA256 before decryption.</summary>
public static class AuthenticatedBackupFileCryptography
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("MAIBAK01");
    private const int IvLength = 16;
    private const int TagLength = 32;
    private const int BufferSize = 1024 * 1024;

    public static async Task EncryptAsync(string sourcePath, string destinationPath, byte[] dataKey,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(dataKey);
        var encKey = DeriveKey(dataKey, "MiscelaneaAri.BackupFile.Encryption.v1");
        var macKey = DeriveKey(dataKey, "MiscelaneaAri.BackupFile.Authentication.v1");
        var iv = RandomNumberGenerator.GetBytes(IvLength);
        try
        {
            await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, macKey);
            await output.WriteAsync(Magic, cancellationToken).ConfigureAwait(false);
            await output.WriteAsync(iv, cancellationToken).ConfigureAwait(false);
            hmac.AppendData(Magic);
            hmac.AppendData(iv);

            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Key = encKey;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            await using (var authenticatedOutput = new HmacWriteStream(output, hmac))
            await using (var crypto = new CryptoStream(authenticatedOutput, aes.CreateEncryptor(), CryptoStreamMode.Write, leaveOpen: true))
            {
                await input.CopyToAsync(crypto, BufferSize, cancellationToken).ConfigureAwait(false);
                await crypto.FlushFinalBlockAsync(cancellationToken).ConfigureAwait(false);
            }

            var tag = hmac.GetHashAndReset();
            await output.WriteAsync(tag, cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            CryptographicOperations.ZeroMemory(tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encKey);
            CryptographicOperations.ZeroMemory(macKey);
            CryptographicOperations.ZeroMemory(iv);
        }
    }

    public static async Task DecryptAsync(string sourcePath, string destinationPath, byte[] dataKey,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(dataKey);
        var encKey = DeriveKey(dataKey, "MiscelaneaAri.BackupFile.Encryption.v1");
        var macKey = DeriveKey(dataKey, "MiscelaneaAri.BackupFile.Authentication.v1");
        try
        {
            await VerifyAuthenticationAsync(sourcePath, macKey, cancellationToken).ConfigureAwait(false);
            await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var header = new byte[Magic.Length + IvLength];
            await input.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
            var cipherLength = input.Length - header.Length - TagLength;
            if (cipherLength <= 0 || cipherLength % IvLength != 0)
                throw new InvalidDataException("El respaldo cifrado tiene un tamaño no válido.");

            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Key = encKey;
            aes.IV = header.AsSpan(Magic.Length, IvLength).ToArray();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            await using var bounded = new LengthLimitedReadStream(input, cipherLength);
            await using var crypto = new CryptoStream(bounded, aes.CreateDecryptor(), CryptoStreamMode.Read);
            await using var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await crypto.CopyToAsync(output, BufferSize, cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            CryptographicOperations.ZeroMemory(header);
        }
        catch
        {
            TryDelete(destinationPath);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encKey);
            CryptographicOperations.ZeroMemory(macKey);
        }
    }

    private static async Task VerifyAuthenticationAsync(string path, byte[] macKey, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (input.Length < Magic.Length + IvLength + IvLength + TagLength)
            throw new InvalidDataException("El respaldo cifrado está incompleto.");

        var header = new byte[Magic.Length + IvLength];
        await input.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        if (!header.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new InvalidDataException("El formato del respaldo cifrado no es compatible.");

        input.Position = 0;
        var authenticatedLength = input.Length - TagLength;
        using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, macKey);
        var buffer = new byte[BufferSize];
        var remaining = authenticatedLength;
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException("El respaldo cifrado está incompleto.");
            hmac.AppendData(buffer, 0, read);
            remaining -= read;
        }

        var expectedTag = hmac.GetHashAndReset();
        var actualTag = new byte[TagLength];
        await input.ReadExactlyAsync(actualTag, cancellationToken).ConfigureAwait(false);
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(expectedTag, actualTag))
                throw new CryptographicException("La autenticación del respaldo cifrado falló; no se descifró el archivo.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(header);
            CryptographicOperations.ZeroMemory(buffer);
            CryptographicOperations.ZeroMemory(expectedTag);
            CryptographicOperations.ZeroMemory(actualTag);
        }
    }

    private static byte[] DeriveKey(byte[] dataKey, string context) =>
        HMACSHA256.HashData(dataKey, Encoding.UTF8.GetBytes(context));

    private static void ValidateKey(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != 32) throw new ArgumentException("La clave de respaldo debe tener 256 bits.", nameof(key));
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class HmacWriteStream(Stream inner, IncrementalHash hmac) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override void Write(byte[] buffer, int offset, int count)
        {
            hmac.AppendData(buffer, offset, count);
            inner.Write(buffer, offset, count);
        }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            hmac.AppendData(buffer.Span);
            await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private sealed class LengthLimitedReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _length;
        private long _remaining;
        public LengthLimitedReadStream(Stream inner, long length)
        {
            _inner = inner;
            _length = length;
            _remaining = length;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position { get => _length - _remaining; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, (int)Math.Min(count, _remaining));
            _remaining -= read;
            return read;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await _inner.ReadAsync(buffer[..(int)Math.Min(buffer.Length, _remaining)], cancellationToken)
                .ConfigureAwait(false);
            _remaining -= read;
            return read;
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
