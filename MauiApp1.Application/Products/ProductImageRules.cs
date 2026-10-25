namespace MauiApp1.Application.Products;

/// <summary>Size and file-signature checks for images transported with product sync operations.</summary>
public static class ProductImageRules
{
    // Product operations are sent as JSON in a single LAN request. This leaves room for Base64
    // encoding and the operation envelope while staying below the coordinator request limit.
    public const int MaximumBytes = 512 * 1024;

    public const long MaximumInitialImportBytes = 24L * 1024 * 1024;

    public static bool IsSupported(byte[]? imageData)
    {
        if (imageData is null) return true;
        if (imageData.Length is 0 or > MaximumBytes) return false;

        var isPng = imageData.Length >= 8 && imageData.AsSpan(0, 8).SequenceEqual(
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var isJpeg = imageData.Length >= 3 && imageData[0] == 0xFF && imageData[1] == 0xD8 && imageData[2] == 0xFF;
        return isPng || isJpeg;
    }
}
