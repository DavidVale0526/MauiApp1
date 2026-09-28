namespace MauiApp1.Infrastructure.Persistence.Entities;

internal sealed class CategoryEntity
{
    public Guid CategoryId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string NormalizedName { get; set; } = string.Empty;
}
