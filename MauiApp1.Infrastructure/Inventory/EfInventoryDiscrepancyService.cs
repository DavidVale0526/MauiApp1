using System.Text.Json;
using MauiApp1.Application.Authentication;
using MauiApp1.Application.Inventory;
using MauiApp1.Application.Storage;
using MauiApp1.Domain.Users;
using MauiApp1.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MauiApp1.Infrastructure.Inventory;

internal sealed class EfInventoryDiscrepancyService(
    IDbContextFactory<LocalDbContext> contextFactory,
    ILocalDeviceIdentity deviceIdentity,
    IAuthenticationService authenticationService) : IInventoryDiscrepancyService
{
    private const string ResolutionOperationType = "Inventory.ShortageResolved";

    public async Task<InventoryDiscrepancySummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        if (authenticationService.CurrentUser?.Role != UserRole.Administrator)
            return new InventoryDiscrepancySummary(0, 0);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var open = context.InventoryDiscrepancies.AsNoTracking().Where(item => !item.IsResolved);
        var count = await open.CountAsync(cancellationToken);
        var shortage = count == 0
            ? 0
            : await open.SumAsync(item => (long?)item.ShortageMilliUnits, cancellationToken) ?? 0;
        return new InventoryDiscrepancySummary(count, shortage);
    }

    public async Task<IReadOnlyList<InventoryDiscrepancyEntry>> GetOpenAsync(CancellationToken cancellationToken = default)
    {
        if (authenticationService.CurrentUser?.Role != UserRole.Administrator)
            return [];

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await (
            from discrepancy in context.InventoryDiscrepancies.AsNoTracking()
            join product in context.Products.AsNoTracking() on discrepancy.ProductId equals product.ProductId
            join sale in context.Sales.AsNoTracking() on discrepancy.OperationId equals sale.OperationId into sales
            from sale in sales.DefaultIfEmpty()
            join account in context.UserAccounts.AsNoTracking() on discrepancy.ActorUserId equals account.UserId into accounts
            from account in accounts.DefaultIfEmpty()
            where !discrepancy.IsResolved
            orderby discrepancy.CreatedAtUtc descending
            select new
            {
                discrepancy.DiscrepancyId,
                discrepancy.OperationId,
                product.Name,
                product.Unit,
                discrepancy.ShortageMilliUnits,
                discrepancy.CreatedAtUtc,
                TotalMinorUnits = (long?)sale.TotalMinorUnits ?? 0,
                UserName = account == null ? null : account.UserName
            }).ToListAsync(cancellationToken);

        return rows.Select(row => new InventoryDiscrepancyEntry(
            row.DiscrepancyId,
            row.OperationId,
            row.Name,
            row.Unit,
            row.ShortageMilliUnits,
            row.CreatedAtUtc,
            row.TotalMinorUnits,
            string.IsNullOrWhiteSpace(row.UserName) ? "Usuario" : row.UserName!)).ToArray();
    }

    public async Task<string?> ResolveAsync(Guid discrepancyId, CancellationToken cancellationToken = default)
    {
        var actor = authenticationService.CurrentUser;
        if (actor is null || actor.Role != UserRole.Administrator)
            return "Solo el Administrador puede marcar un faltante como resuelto.";

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var discrepancy = await context.InventoryDiscrepancies
            .SingleOrDefaultAsync(item => item.DiscrepancyId == discrepancyId, cancellationToken);
        if (discrepancy is null)
            return "El faltante ya no está en este dispositivo. Actualiza la lista.";
        if (discrepancy.IsResolved)
            return null;

        var operationId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new InventoryShortageResolvedPayload(
            operationId, discrepancy.OperationId, discrepancy.ProductId));
        var outbox = new EfOperationOutbox(context, deviceIdentity);
        await outbox.EnqueueAsync(new NewOutboxOperation(
            operationId, actor.Id.Value, ResolutionOperationType, payload), cancellationToken);

        discrepancy.IsResolved = true;
        await context.SaveChangesAsync(cancellationToken);
        return null;
    }

    private sealed record InventoryShortageResolvedPayload(
        Guid OperationId, Guid SaleOperationId, Guid ProductId);
}
