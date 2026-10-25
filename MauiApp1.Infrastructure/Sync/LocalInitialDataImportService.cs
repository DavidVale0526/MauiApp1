using System.Collections.Concurrent;
using MauiApp1.Application.Authentication;
using MauiApp1.Application.Products;
using MauiApp1.Application.Sync;
using MauiApp1.Application.Storage;
using MauiApp1.Infrastructure.Persistence;
using MauiApp1.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MauiApp1.Infrastructure.Sync;

public sealed class LocalInitialDataImportService(
    IDbContextFactory<LocalDbContext> contextFactory,
    ILocalDeviceIdentity deviceIdentity,
    IAuthenticationService authenticationService,
    IInitialDataImportClient coordinatorClient,
    ILocalDataChangeNotifier changeNotifier) : ILocalInitialDataImportService
{
    private readonly ConcurrentDictionary<Guid, InitialDataImportSnapshot> _snapshots = new();

    public async Task<InitialImportClientResult<InitialImportPreview>> PreviewCurrentDeviceAsync(
        CancellationToken cancellationToken = default)
    {
        if (authenticationService.CurrentUser is null)
            return new(false, null, "Inicia sesión como Administrador para analizar los datos de este dispositivo.");

        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            if (await context.DeviceSyncConflicts.AsNoTracking().AnyAsync(item => !item.IsResolved, cancellationToken))
                return new(false, null, "Primero revisa los conflictos pendientes de este dispositivo. Después podrás importar sus datos previos.");
            var usedActorIds = (await context.Purchases.AsNoTracking().Select(item => item.ActorUserId).ToListAsync(cancellationToken))
                .Concat(await context.Sales.AsNoTracking().Select(item => item.ActorUserId).ToListAsync(cancellationToken))
                .Concat(await context.InventoryMovements.AsNoTracking().Select(item => item.ActorUserId).ToListAsync(cancellationToken))
                .Concat(await context.Payments.AsNoTracking().Select(item => item.ActorUserId).ToListAsync(cancellationToken))
                .Concat(await context.InventoryDiscrepancies.AsNoTracking().Select(item => item.ActorUserId).ToListAsync(cancellationToken))
                .Distinct().ToArray();
            var actors = await context.UserAccounts.AsNoTracking().Where(item => usedActorIds.Contains(item.UserId))
                .Select(item => new InitialImportActor(item.UserId, item.UserName, item.Role)).ToListAsync(cancellationToken);
            var categories = await context.Categories.AsNoTracking()
                .Select(item => new InitialImportCategory(item.CategoryId, item.Name)).ToListAsync(cancellationToken);
            var products = await context.Products.AsNoTracking()
                .Select(item => new InitialImportProduct(item.ProductId, item.Name, item.SalePriceMinorUnits,
                    item.StockMilliUnits, item.Unit, item.CategoryId, item.MinimumStockMilliUnits, item.IsActive,
                    item.ImageData))
                .ToListAsync(cancellationToken);
            var customers = await context.Customers.AsNoTracking()
                .Select(item => new InitialImportCustomer(item.CustomerId, item.Name, item.Phone, item.CreatedAtUtc))
                .ToListAsync(cancellationToken);

            var purchaseHeaders = await context.Purchases.AsNoTracking().ToListAsync(cancellationToken);
            var purchaseLines = await context.PurchaseLines.AsNoTracking().ToListAsync(cancellationToken);
            var purchases = purchaseHeaders.Select(item => new InitialImportPurchase(item.OperationId,
                AsUtc(item.OccurredAtUtc), item.ActorUserId,
                purchaseLines.Where(line => line.OperationId == item.OperationId).OrderBy(line => line.LineNumber)
                    .Select(line => new InitialImportPurchaseLine(line.LineNumber, line.ProductId,
                        line.ProductNameSnapshot, line.UnitSnapshot, line.QuantityMilliUnits)).ToArray())).ToArray();

            var saleHeaders = await context.Sales.AsNoTracking().ToListAsync(cancellationToken);
            var saleLines = await context.SaleLines.AsNoTracking().ToListAsync(cancellationToken);
            var sales = saleHeaders.Select(item => new InitialImportSale(item.OperationId, AsUtc(item.OccurredAtUtc),
                item.ActorUserId, item.TotalMinorUnits, item.IsCredit, item.CustomerId,
                saleLines.Where(line => line.OperationId == item.OperationId).OrderBy(line => line.LineNumber)
                    .Select(line => new InitialImportSaleLine(line.LineNumber, line.ProductId,
                        line.ProductNameSnapshot, line.UnitSnapshot, line.QuantityMilliUnits,
                        line.SalePriceMinorUnits, line.LineTotalMinorUnits)).ToArray())).ToArray();

            var receivables = await context.Receivables.AsNoTracking()
                .Select(item => new InitialImportReceivable(item.OperationId, item.CustomerId,
                    item.OriginalAmountMinorUnits, item.OutstandingMinorUnits, item.DueAtUtc)).ToListAsync(cancellationToken);
            var movements = await context.InventoryMovements.AsNoTracking()
                .Select(item => new InitialImportMovement(item.MovementId, item.OperationId, item.LineNumber,
                    item.ProductId, item.ProductNameSnapshot, item.UnitSnapshot, item.Type,
                    item.QuantityMilliUnits, item.Reason, item.OccurredAtUtc, item.ActorUserId))
                .ToListAsync(cancellationToken);
            var payments = await context.Payments.AsNoTracking()
                .Select(item => new InitialImportPayment(item.PaymentId, item.OperationId, item.ActorUserId,
                    item.AmountMinorUnits, item.OccurredAtUtc)).ToListAsync(cancellationToken);
            var discrepancies = await context.InventoryDiscrepancies.AsNoTracking()
                .Select(item => new InitialImportDiscrepancy(item.DiscrepancyId, item.OperationId,
                    item.ProductId, item.ActorUserId, item.ShortageMilliUnits, item.CreatedAtUtc, item.IsResolved))
                .ToListAsync(cancellationToken);

            var snapshot = new InitialDataImportSnapshot(Guid.NewGuid(), 1,
                await deviceIdentity.GetDeviceIdAsync(cancellationToken), DateTime.UtcNow,
                actors, categories, products, customers, purchases, sales, receivables, movements, payments, discrepancies);
            if (snapshot.Products.Sum(item => (long)(item.ImageData?.Length ?? 0)) > ProductImageRules.MaximumInitialImportBytes)
                return new(false, null,
                    "Las imágenes de este equipo superan el máximo combinado de 24 MB para la importación inicial. Reduce el tamaño de algunas imágenes y vuelve a analizar.");
            var pendingOperations = await context.OutboxMessages.AsNoTracking()
                .Where(item => (int)item.Status == 0 || (int)item.Status == 2)
                .ToListAsync(cancellationToken);
            if (pendingOperations.Any(item => !InitialImportOutboxMatcher.IsCoveredBySnapshot(
                    item.OperationType, item.OperationId, item.PayloadJson, snapshot)))
                return new(false, null,
                    "Hay cambios pendientes o conflictos que no forman parte de los registros capturados. Sincronízalos o resuélvelos antes de importar.");

            var result = await coordinatorClient.PreviewAsync(snapshot, cancellationToken);
            if (result.Succeeded && result.Value is not null)
            {
                _snapshots.Clear();
                _snapshots[snapshot.BatchId] = snapshot;
            }
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new(false, null, "No se pudieron preparar los datos de este dispositivo. No se modificó la base local.");
        }
    }

    public Task<InitialImportClientResult<InitialImportApplyResult>> ApplyAsync(
        InitialImportApplyRequest request, CancellationToken cancellationToken = default) =>
        ApplyAndRefreshLocalStockAsync(request, cancellationToken);

    private async Task<InitialImportClientResult<InitialImportApplyResult>> ApplyAndRefreshLocalStockAsync(
        InitialImportApplyRequest request, CancellationToken cancellationToken)
    {
        var result = await coordinatorClient.ApplyAsync(request, cancellationToken);
        if (result.Value is null) return result;
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            foreach (var stock in result.Value.ProductStocks ?? [])
            {
                var product = await context.Products.SingleOrDefaultAsync(item => item.ProductId == stock.ProductId, cancellationToken);
                if (product is not null) product.StockMilliUnits = stock.StockMilliUnits;
            }
            if (result.Value.Completed && _snapshots.TryGetValue(request.BatchId, out var snapshot))
            {
                var reconciledOperations = await context.OutboxMessages
                    .Where(item => (int)item.Status == 0 || (int)item.Status == 2)
                    .ToListAsync(cancellationToken);
                foreach (var operation in reconciledOperations.Where(item => InitialImportOutboxMatcher.IsCoveredBySnapshot(
                             item.OperationType, item.OperationId, item.PayloadJson, snapshot)))
                {
                    operation.Status = OutboxStatus.Confirmed;
                    operation.ConfirmedAtUtc = DateTime.UtcNow;
                    operation.LastError = null;
                }
                _snapshots.TryRemove(request.BatchId, out _);
            }
            await context.SaveChangesAsync(cancellationToken);
            changeNotifier.NotifyDataChanged();
        }
        catch
        {
            return new(false, result.Value,
                "La importación llegó al coordinador, pero este dispositivo no pudo actualizar algunos saldos. Sincroniza para volver a consultarlos.");
        }
        return result;
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

}
