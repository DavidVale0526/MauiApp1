using System.Security.Cryptography;
using System.Text;

namespace MauiApp1.Infrastructure.Authentication;

internal sealed class Pbkdf2PasswordHasher
{
    public const int Iterations = 600_000;
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;

    public (byte[] Salt, byte[] Hash) Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, HashSizeBytes);
        return (salt, hash);
    }

    public bool Verify(string password, byte[] salt, byte[] expectedHash, int iterations)
    {
        var actualHash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, expectedHash.Length);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
