using System.Security.Cryptography;
using MauiApp1.Application.Authentication;

namespace MauiApp1.Infrastructure.Authentication;

public sealed class RecoveryCodeGenerator : IRecoveryCodeGenerator
{
    private const int CodeBytes = 16;
    private const int NormalizedLength = CodeBytes * 2;

    public string Generate()
    {
        var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(CodeBytes));
        return string.Join('-', Enumerable.Range(0, 8).Select(index => raw.Substring(index * 4, 4)));
    }

    public static bool TryNormalize(string? code, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(code)) return false;

        var characters = code.Where(character => character is not ('-' or ' ')).ToArray();
        if (characters.Length != NormalizedLength || characters.Any(character => !Uri.IsHexDigit(character)))
            return false;

        normalized = new string(characters).ToUpperInvariant();
        return true;
    }
}
