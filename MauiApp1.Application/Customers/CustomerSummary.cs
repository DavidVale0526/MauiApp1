namespace MauiApp1.Application.Customers;

public sealed record CustomerSummary(Guid CustomerId, string Name, string? Phone, long OutstandingMinorUnits);
