namespace MauiApp1.Application.Categories;

public interface ICategoryService
{
    Task<IReadOnlyList<CategorySummary>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<CategoryOperationResult> CreateAsync(string name, CancellationToken cancellationToken = default);

    Task<CategoryOperationResult> RenameAsync(Guid categoryId, string name, CancellationToken cancellationToken = default);

    Task<CategoryOperationResult> DeleteAsync(Guid categoryId, CancellationToken cancellationToken = default);
}
