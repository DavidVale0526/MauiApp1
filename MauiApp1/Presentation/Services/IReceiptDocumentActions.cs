namespace MauiApp1.Presentation.Services;

public interface IReceiptDocumentActions
{
    Task<string?> SavePdfAsync(string fileName, byte[] contents, CancellationToken cancellationToken = default);

    Task SharePdfAsync(string path);

    Task OpenPdfAsync(string path);
}
