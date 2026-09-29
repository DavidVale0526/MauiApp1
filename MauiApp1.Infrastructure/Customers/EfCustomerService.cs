using System.Text.Json;
using MauiApp1.Application.Authentication;
using MauiApp1.Application.Customers;
using MauiApp1.Application.Storage;
using MauiApp1.Infrastructure.Persistence;
using MauiApp1.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MauiApp1.Infrastructure.Customers;

internal sealed class EfCustomerService(
    IDbContextFactory<LocalDbContext> contextFactory,
    ILocalDeviceIdentity deviceIdentity,
    IAuthenticationService authenticationService) : ICustomerService
{
    public async Task<(bool Succeeded, string? ErrorMessage)> CreateAsync(
        string name, string? phone, CancellationToken cancellationToken = default)
    {
        var actor = authenticationService.CurrentUser;
        if (actor is null)
        {
            return (false, "Inicia sesión para registrar clientes.");
        }

        var cleanName = name?.Trim();
        if (string.IsNullOrWhiteSpace(cleanName) || cleanName.Length > 120)
        {
            return (false, "Escribe un nombre de cliente de hasta 120 caracteres.");
        }

        var normalizedName = cleanName.ToUpperInvariant();
        var cleanPhone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        if (cleanPhone?.Length > 40)
        {
            return (false, "El teléfono no puede superar 40 caracteres.");
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (await context.Customers.AnyAsync(item => item.NormalizedName == normalizedName, cancellationToken))
        {
            return (false, "Ya existe un cliente con ese nombre.");
        }

        var customerId = Guid.NewGuid();
        context.Customers.Add(new CustomerEntity
        {
            CustomerId = customerId,
            Name = cleanName,
            NormalizedName = normalizedName,
            Phone = cleanPhone,
            CreatedAtUtc = DateTime.UtcNow
        });
        var outbox = new EfOperationOutbox(context, deviceIdentity);
        await outbox.EnqueueAsync(new NewOutboxOperation(
            Guid.NewGuid(), actor.Id.Value, "Customer.Created",
            JsonSerializer.Serialize(new { CustomerId = customerId, Name = cleanName, Phone = cleanPhone })), cancellationToken);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return (true, null);
        }
        catch (DbUpdateException)
        {
            return (false, "No se pudo guardar el cliente. Comprueba si el nombre ya está registrado e inténtalo de nuevo.");
        }
    }

    public async Task<IReadOnlyList<CustomerSummary>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        if (authenticationService.CurrentUser is null)
        {
            throw new UnauthorizedAccessException("Inicia sesión para consultar clientes.");
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var customers = await context.Customers.AsNoTracking()
            .OrderBy(item => item.NormalizedName)
            .Select(item => new { item.CustomerId, item.Name, item.Phone })
            .ToListAsync(cancellationToken);
        var debts = await context.Receivables.AsNoTracking()
            .GroupBy(item => item.CustomerId)
            .Select(group => new { CustomerId = group.Key, Outstanding = group.Sum(item => item.OutstandingMinorUnits) })
            .ToDictionaryAsync(item => item.CustomerId, item => item.Outstanding, cancellationToken);

        return customers.Select(customer => new CustomerSummary(
            customer.CustomerId, customer.Name, customer.Phone,
            debts.GetValueOrDefault(customer.CustomerId))).ToArray();
    }
}
