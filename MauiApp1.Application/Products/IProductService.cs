namespace MauiApp1.Application.Products;

public interface IProductService
{
    Task<IReadOnlyList<ProductSummary>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<ProductOperationResult> CreateAsync(ProductDraft draft, CancellationToken cancellationToken = default);

    Task<ProductOperationResult> UpdateAsync(
        Guid productId,
        ProductDraft draft,
        CancellationToken cancellationToken = default);

    Task<ProductOperationResult> SetActiveAsync(
        Guid productId,
        bool isActive,
        CancellationToken cancellationToken = default);
}
