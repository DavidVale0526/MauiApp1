namespace MauiApp1.Application.Sync;

public interface IInitialDataImportClient
{
    Task<InitialImportClientResult<InitialImportPreview>> PreviewAsync(
        InitialDataImportSnapshot snapshot, CancellationToken cancellationToken = default);

    Task<InitialImportClientResult<InitialImportApplyResult>> ApplyAsync(
        InitialImportApplyRequest request, CancellationToken cancellationToken = default);
}

public interface ILocalInitialDataImportService
{
    Task<InitialImportClientResult<InitialImportPreview>> PreviewCurrentDeviceAsync(
        CancellationToken cancellationToken = default);

    Task<InitialImportClientResult<InitialImportApplyResult>> ApplyAsync(
        InitialImportApplyRequest request, CancellationToken cancellationToken = default);
}
