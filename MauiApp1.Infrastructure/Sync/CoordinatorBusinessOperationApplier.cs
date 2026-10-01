using System.Text.Json;
using MauiApp1.Application.Categories;
using MauiApp1.Application.Customers;
using MauiApp1.Application.Inventory;
using MauiApp1.Application.Products;
using MauiApp1.Application.Purchases;
using MauiApp1.Application.Sales;
using MauiApp1.Domain.Inventory;
using MauiApp1.Infrastructure.Persistence;
using MauiApp1.Infrastructure.Persistence.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MauiApp1.Infrastructure.Sync;

/// <summary>Applies a received business operation to the coordinator's central SQLite database.</summary>
public sealed class CoordinatorBusinessOperationApplier
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<string?> ApplyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        string operationType,
        string payloadJson,
        DateTime createdAtUtc,
        Guid actorUserId,
        CancellationToken cancellationToken,
        bool forceCatalogSnapshot = false)
    {
        var options = new DbContextOptionsBuilder<LocalDbContext>().UseSqlite(connection).Options;
        await using var context = new LocalDbContext(options);
        await context.Database.UseTransactionAsync(transaction, cancellationToken);
        try
        {
            switch (operationType)
            {
                case "Category.Created":
                case "Category.Renamed":
                case "Category.Resolved":
                    await ApplyCategoryAsync(context, operationType, payloadJson, cancellationToken, forceCatalogSnapshot);
                    break;
                case "Category.Deleted":
                    await ApplyCategoryDeleteAsync(context, payloadJson, cancellationToken);
                    break;
                case "Product.Created":
                case "Product.Updated":
                case "Product.Deactivated":
                case "Product.Reactivated":
                case "Product.Resolved":
                    await ApplyProductAsync(context, operationType, payloadJson, cancellationToken, forceCatalogSnapshot);
                    break;
                case "Customer.Created":
                    await ApplyCustomerAsync(context, payloadJson, createdAtUtc, cancellationToken);
                    break;
                case "Purchase.Recorded":
                    await ApplyPurchaseAsync(context, operationId, payloadJson, actorUserId, cancellationToken);
                    break;
                case "Sale.Recorded":
                    await ApplySaleAsync(context, operationId, payloadJson, actorUserId, cancellationToken);
                    break;
                case "Inventory.Recorded":
                    await ApplyInventoryAsync(context, operationId, payloadJson, actorUserId, cancellationToken);
                    break;
                case "Credit.PaymentRecorded":
                    await ApplyPaymentAsync(context, payloadJson, actorUserId, cancellationToken);
                    break;
                default:
                    return $"Tipo de operación no admitido: {operationType}.";
            }

            await context.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (BusinessOperationConflictException exception)
        {
            return exception.Message;
        }
        catch (DbUpdateException exception)
        {
            return $"La operación entra en conflicto con datos centrales: {exception.GetBaseException().Message}";
        }
        catch (JsonException)
        {
            return "El contenido de la operación no coincide con el formato esperado.";
        }
        catch (OverflowException)
        {
            return "La operación contiene una cantidad o importe fuera del rango permitido.";
        }
    }

    private static async Task ApplyCategoryAsync(LocalDbContext context, string operationType, string json, CancellationToken ct, bool force)
    {
        var payload = Read<CategoryChangedPayload>(json);
        var name = payload.Name.Trim();
        if (payload.CategoryId == Guid.Empty || name.Length == 0)
            throw new BusinessOperationConflictException("La categoría contiene datos inválidos.");
        var existing = await context.Categories.SingleOrDefaultAsync(item => item.CategoryId == payload.CategoryId, ct);
        var normalized = name.ToUpperInvariant();
        if (existing is null)
        {
            if (await context.Categories.AnyAsync(item => item.NormalizedName == normalized, ct))
                throw new BusinessOperationConflictException("Ya existe otra categoría con ese nombre.");
            context.Categories.Add(new CategoryEntity { CategoryId = payload.CategoryId, Name = name, NormalizedName = normalized });
        }
        else if (existing.Name != name)
        {
            if (operationType == "Category.Created")
                throw new BusinessOperationConflictException("Ya existe una categoría con ese identificador y nombre diferente.");
            if (operationType != "Category.Resolved" && !force && (payload.ExpectedName is null || existing.Name != payload.ExpectedName))
                throw new BusinessOperationConflictException("La categoría cambió en otro dispositivo. Se conserva el cambio para resolución del Administrador.");
            if (await context.Categories.AnyAsync(item => item.CategoryId != payload.CategoryId && item.NormalizedName == normalized, ct))
                throw new BusinessOperationConflictException("Ya existe otra categoría con ese nombre.");
            existing.Name = name;
            existing.NormalizedName = normalized;
        }
    }

    private static async Task ApplyCategoryDeleteAsync(LocalDbContext context, string json, CancellationToken ct)
    {
        var payload = Read<CategoryDeletedPayload>(json);
        var category = await context.Categories.SingleOrDefaultAsync(item => item.CategoryId == payload.CategoryId, ct);
        if (category is null) return;
        if (await context.Products.AnyAsync(item => item.CategoryId == payload.CategoryId, ct))
            throw new BusinessOperationConflictException("La categoría está asignada a productos y no puede eliminarse.");
        context.Categories.Remove(category);
    }

    private static async Task ApplyProductAsync(LocalDbContext context, string operationType, string json, CancellationToken ct, bool force)
    {
        var payload = Read<ProductChangedPayload>(json);
        var product = await context.Products.SingleOrDefaultAsync(item => item.ProductId == payload.ProductId, ct);
        if (product is null)
        {
            if (!await context.Categories.AnyAsync(item => item.CategoryId == payload.CategoryId, ct))
                throw new BusinessOperationConflictException("La categoría del producto aún no existe en el coordinador.");
            product = new ProductEntity { ProductId = payload.ProductId, StockMilliUnits = operationType == "Product.Resolved" ? payload.StockMilliUnits : 0 };
            context.Products.Add(product);
            product.Name = payload.Name;
            product.SalePriceMinorUnits = payload.SalePriceMinorUnits;
            product.Unit = payload.Unit;
            product.CategoryId = payload.CategoryId;
            product.MinimumStockMilliUnits = payload.MinimumStockMilliUnits;
            product.IsActive = payload.IsActive;
            return;
        }
        if (operationType == "Product.Created")
        {
            if (!CatalogMatches(product, payload))
                throw new BusinessOperationConflictException("Ya existe un producto con ese identificador y datos diferentes.");
            return;
        }
        if (operationType != "Product.Resolved" && !force && (payload.Expected is null || !CatalogMatches(product, payload.Expected)))
            throw new BusinessOperationConflictException("El producto cambió en otro dispositivo. Se conserva esta edición para que el Administrador resuelva el conflicto.");
        if (!await context.Categories.AnyAsync(item => item.CategoryId == payload.CategoryId, ct))
            throw new BusinessOperationConflictException("La categoría del producto no existe en el coordinador.");
        product.Name = payload.Name;
        product.SalePriceMinorUnits = payload.SalePriceMinorUnits;
        // Stock is changed only by durable purchase, sale, and inventory operations.
        product.Unit = payload.Unit;
        product.CategoryId = payload.CategoryId;
        product.MinimumStockMilliUnits = payload.MinimumStockMilliUnits;
        product.IsActive = payload.IsActive;
    }

    private static bool CatalogMatches(ProductEntity product, ProductChangedPayload payload) =>
        product.Name == payload.Name && product.SalePriceMinorUnits == payload.SalePriceMinorUnits && product.Unit == payload.Unit &&
        product.CategoryId == payload.CategoryId && product.MinimumStockMilliUnits == payload.MinimumStockMilliUnits &&
        product.IsActive == payload.IsActive;

    private static bool CatalogMatches(ProductEntity product, ProductCatalogSnapshot snapshot) =>
        product.Name == snapshot.Name && product.SalePriceMinorUnits == snapshot.SalePriceMinorUnits && product.Unit == snapshot.Unit &&
        product.CategoryId == snapshot.CategoryId && product.MinimumStockMilliUnits == snapshot.MinimumStockMilliUnits &&
        product.IsActive == snapshot.IsActive;

    private static async Task ApplyCustomerAsync(LocalDbContext context, string json, DateTime createdAtUtc, CancellationToken ct)
    {
        var payload = Read<CustomerCreatedPayload>(json);
        var id = payload.CustomerId;
        var name = payload.Name.Trim();
        var phone = payload.Phone;
        var normalized = name.ToUpperInvariant();
        if (id == Guid.Empty || name.Length == 0) throw new BusinessOperationConflictException("El cliente contiene datos inválidos.");
        if (await context.Customers.AnyAsync(item => item.CustomerId == id, ct)) return;
        if (await context.Customers.AnyAsync(item => item.NormalizedName == normalized, ct))
            throw new BusinessOperationConflictException("Ya existe un cliente con ese nombre.");
        context.Customers.Add(new CustomerEntity { CustomerId = id, Name = name, NormalizedName = normalized, Phone = phone, CreatedAtUtc = createdAtUtc });
    }

    private static async Task ApplyPurchaseAsync(LocalDbContext context, Guid operationId, string json, Guid actor, CancellationToken ct)
    {
        var payload = Read<PurchaseRecordedPayload>(json);
        if (payload.OperationId != operationId || payload.Lines.Count == 0 || payload.OccurredAtUtc.Kind != DateTimeKind.Utc ||
            payload.Lines.Select(item => item.LineNumber).Distinct().Count() != payload.Lines.Count)
            throw new BusinessOperationConflictException("La compra está incompleta.");
        var purchase = new PurchaseEntity { OperationId = operationId, OccurredAtUtc = payload.OccurredAtUtc, ActorUserId = actor };
        context.Purchases.Add(purchase);
        foreach (var line in payload.Lines.OrderBy(item => item.LineNumber))
        {
            var product = await context.Products.SingleOrDefaultAsync(item => item.ProductId == line.ProductId, ct)
                ?? throw new BusinessOperationConflictException("Un producto de la compra no existe en el coordinador.");
            if (line.QuantityMilliUnits <= 0) throw new BusinessOperationConflictException("La cantidad de compra debe ser positiva.");
            var name = string.IsNullOrWhiteSpace(line.ProductNameSnapshot) ? product.Name : line.ProductNameSnapshot;
            var unit = string.IsNullOrWhiteSpace(line.UnitSnapshot) ? product.Unit : line.UnitSnapshot;
            context.PurchaseLines.Add(new PurchaseLineEntity { OperationId = operationId, LineNumber = line.LineNumber,
                ProductId = product.ProductId, ProductNameSnapshot = name, UnitSnapshot = unit, QuantityMilliUnits = line.QuantityMilliUnits });
            product.StockMilliUnits = checked(product.StockMilliUnits + line.QuantityMilliUnits);
            context.InventoryMovements.Add(new InventoryMovementEntity { MovementId = Guid.NewGuid(), OperationId = operationId,
                LineNumber = line.LineNumber, ProductId = product.ProductId, ProductNameSnapshot = name, UnitSnapshot = unit,
                Type = (int)InventoryMovementType.PurchaseEntry, QuantityMilliUnits = line.QuantityMilliUnits,
                OccurredAtUtc = payload.OccurredAtUtc, ActorUserId = actor });
        }
    }

    private static async Task ApplySaleAsync(LocalDbContext context, Guid operationId, string json, Guid actor, CancellationToken ct)
    {
        var payload = Read<SaleRecordedPayload>(json);
        if (payload.OperationId != operationId || payload.Lines.Count == 0 || payload.TotalMinorUnits < 0 ||
            payload.OccurredAtUtc.Kind != DateTimeKind.Utc || payload.Lines.Select(item => item.LineNumber).Distinct().Count() != payload.Lines.Count ||
            payload.Lines.Sum(item => item.LineTotalMinorUnits) != payload.TotalMinorUnits)
            throw new BusinessOperationConflictException("La venta está incompleta.");
        if (payload.IsCredit && (payload.CustomerId is null || payload.CreditDueAtUtc is null || payload.CreditDueAtUtc.Value.Kind != DateTimeKind.Utc))
            throw new BusinessOperationConflictException("La venta a crédito requiere cliente y vencimiento.");
        if (payload.CustomerId is Guid customerId && !await context.Customers.AnyAsync(item => item.CustomerId == customerId, ct))
            throw new BusinessOperationConflictException("El cliente de la venta no existe en el coordinador.");
        context.Sales.Add(new SaleEntity { OperationId = operationId, OccurredAtUtc = payload.OccurredAtUtc, ActorUserId = actor,
            TotalMinorUnits = payload.TotalMinorUnits, IsCredit = payload.IsCredit, CustomerId = payload.CustomerId });
        foreach (var line in payload.Lines.OrderBy(item => item.LineNumber))
        {
            var product = await context.Products.SingleOrDefaultAsync(item => item.ProductId == line.ProductId, ct)
                ?? throw new BusinessOperationConflictException("Un producto de la venta no existe en el coordinador.");
            if (line.QuantityMilliUnits <= 0 || line.SalePriceMinorUnits < 0 || line.LineTotalMinorUnits < 0)
                throw new BusinessOperationConflictException("Un renglón de venta contiene importes o cantidades inválidos.");
            var qty = Math.Min(product.StockMilliUnits, line.QuantityMilliUnits);
            product.StockMilliUnits -= qty;
            var shortage = line.QuantityMilliUnits - qty;
            if (shortage > 0)
                context.InventoryDiscrepancies.Add(new InventoryDiscrepancyEntity { DiscrepancyId = Guid.NewGuid(), OperationId = operationId,
                    ProductId = product.ProductId, ActorUserId = actor, ShortageMilliUnits = shortage, CreatedAtUtc = payload.OccurredAtUtc, IsResolved = false });
            context.SaleLines.Add(new SaleLineEntity { OperationId = operationId, LineNumber = line.LineNumber, ProductId = product.ProductId,
                ProductNameSnapshot = line.ProductNameSnapshot, UnitSnapshot = line.UnitSnapshot, QuantityMilliUnits = line.QuantityMilliUnits,
                SalePriceMinorUnits = line.SalePriceMinorUnits, LineTotalMinorUnits = line.LineTotalMinorUnits });
            context.InventoryMovements.Add(new InventoryMovementEntity { MovementId = Guid.NewGuid(), OperationId = operationId, LineNumber = line.LineNumber,
                ProductId = product.ProductId, ProductNameSnapshot = line.ProductNameSnapshot, UnitSnapshot = line.UnitSnapshot,
                Type = (int)InventoryMovementType.SaleExit, QuantityMilliUnits = line.QuantityMilliUnits, OccurredAtUtc = payload.OccurredAtUtc, ActorUserId = actor });
        }
        if (payload.IsCredit)
            context.Receivables.Add(new ReceivableEntity { OperationId = operationId, CustomerId = payload.CustomerId!.Value,
                OriginalAmountMinorUnits = payload.TotalMinorUnits, OutstandingMinorUnits = payload.TotalMinorUnits, DueAtUtc = payload.CreditDueAtUtc!.Value });
    }

    private static async Task ApplyInventoryAsync(LocalDbContext context, Guid operationId, string json, Guid actor, CancellationToken ct)
    {
        var payload = Read<InventoryOperationPayload>(json);
        if (payload.OperationId != operationId || payload.Lines.Count == 0 || payload.OccurredAtUtc.Kind != DateTimeKind.Utc ||
            payload.Lines.Select(item => item.LineNumber).Distinct().Count() != payload.Lines.Count ||
            payload.Lines.Select(item => item.Type).Distinct().Count() != 1)
            throw new BusinessOperationConflictException("El movimiento de inventario está incompleto.");
        foreach (var line in payload.Lines.OrderBy(item => item.LineNumber))
        {
            var product = await context.Products.SingleOrDefaultAsync(item => item.ProductId == line.ProductId, ct)
                ?? throw new BusinessOperationConflictException("Un producto del movimiento no existe en el coordinador.");
            if (line.QuantityMilliUnits <= 0) throw new BusinessOperationConflictException("La cantidad debe ser positiva.");
            var movementType = line.Type switch { InventoryMovementType.PurchaseEntry => InventoryMovementType.PurchaseEntry,
                InventoryMovementType.SaleExit => InventoryMovementType.SaleExit, InventoryMovementType.OtherExit => InventoryMovementType.OtherExit,
                _ => throw new BusinessOperationConflictException("El tipo de movimiento no es válido.") };
            var delta = movementType == InventoryMovementType.PurchaseEntry ? line.QuantityMilliUnits : -line.QuantityMilliUnits;
            if (delta < 0 && product.StockMilliUnits < line.QuantityMilliUnits)
                throw new BusinessOperationConflictException("La salida supera la existencia central y requiere revisión del Administrador.");
            if (movementType == InventoryMovementType.OtherExit && !IsValidOtherExitReason(line.Reason))
                throw new BusinessOperationConflictException("El motivo de salida no es válido. Usa Vencimiento, Daño, Pérdida u Otro: con una descripción.");
            if (movementType != InventoryMovementType.OtherExit && line.Reason is not null)
                throw new BusinessOperationConflictException("Solo las salidas distintas de una venta llevan motivo.");
            product.StockMilliUnits = checked(product.StockMilliUnits + delta);
            context.InventoryMovements.Add(new InventoryMovementEntity { MovementId = Guid.NewGuid(), OperationId = operationId, LineNumber = line.LineNumber,
                ProductId = product.ProductId, ProductNameSnapshot = line.ProductNameSnapshot ?? product.Name, UnitSnapshot = line.UnitSnapshot ?? product.Unit,
                Type = (int)movementType, QuantityMilliUnits = line.QuantityMilliUnits, Reason = line.Reason,
                OccurredAtUtc = payload.OccurredAtUtc, ActorUserId = actor });
        }
    }

    private static async Task ApplyPaymentAsync(LocalDbContext context, string json, Guid actor, CancellationToken ct)
    {
        var payload = Read<PaymentRecordedPayload>(json);
        var paymentId = payload.PaymentId;
        var operationId = payload.OperationId;
        var amount = payload.AmountMinorUnits;
        var occurred = payload.OccurredAtUtc;
        var receivable = await context.Receivables.SingleOrDefaultAsync(item => item.OperationId == operationId, ct)
            ?? throw new BusinessOperationConflictException("La cuenta por cobrar aún no existe en el coordinador.");
        if (amount <= 0 || amount > receivable.OutstandingMinorUnits || occurred.Kind != DateTimeKind.Utc)
            throw new BusinessOperationConflictException("El abono supera el saldo central o contiene un monto inválido.");
        receivable.OutstandingMinorUnits -= amount;
        context.Payments.Add(new PaymentEntity { PaymentId = paymentId, OperationId = operationId, ActorUserId = actor, AmountMinorUnits = amount, OccurredAtUtc = occurred });
    }

    private static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonOptions)
        ?? throw new BusinessOperationConflictException("El contenido de la operación está vacío.");

    private static bool IsValidOtherExitReason(string? reason) =>
        reason is "Vencimiento" or "Daño" or "Pérdida" ||
        (reason is not null && reason.StartsWith("Otro: ", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(reason[6..]));

    private sealed record CustomerCreatedPayload(Guid CustomerId, string Name, string? Phone);
    private sealed record PaymentRecordedPayload(Guid PaymentId, Guid OperationId, Guid ActorUserId, long AmountMinorUnits, DateTime OccurredAtUtc);
}

public sealed class BusinessOperationConflictException(string message) : Exception(message);
