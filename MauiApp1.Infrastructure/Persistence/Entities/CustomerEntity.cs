namespace MauiApp1.Infrastructure.Persistence.Entities;

internal sealed class CustomerEntity
{
    public Guid CustomerId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string NormalizedName { get; set; } = string.Empty;

    public string? Phone { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
