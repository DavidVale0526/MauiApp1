namespace MauiApp1.Application.Customers;

public interface ICustomerService
{
    Task<(bool Succeeded, string? ErrorMessage)> CreateAsync(
        string name, string? phone, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerSummary>> GetAllAsync(CancellationToken cancellationToken = default);
}
