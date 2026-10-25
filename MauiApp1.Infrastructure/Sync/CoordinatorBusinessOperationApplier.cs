using System.Text.Json;
using MauiApp1.Application.Categories;
using MauiApp1.Application.Customers;
using MauiApp1.Application.Inventory;
using MauiApp1.Application.Products;
using MauiApp1.Application.Purchases;
using MauiApp1.Application.Sales;
using MauiApp1.Application.Sync;
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
                case "Inventory.MixedWasteRecorded":
                    await ApplyMixedWasteAsync(context, operationId, payloadJson, actorUserId, cancellationToken);
                    break;
                case "Credit.PaymentRecorded":
                    await ApplyPaymentAsync(context, payloadJson, actorUserId, cancellationToken);
                    break;
                case "Import.Category":
                    await ApplyImportedCategoryAsync(context, payloadJson, cancellationToken);
                    break;
                case "Import.Product":
                    await ApplyImportedProductAsync(context, payloadJson, cancellationToken);
                    break;
                case "Import.Customer":
                    await ApplyImportedCustomerAsync(context, payloadJson, cancellationToken);
                    break;
                case "Import.Purchase":
                    await ApplyImportedPurchaseAsync(context, payloadJson, actorUserId, cancellationToken);
                    break;
                case "Import.Sale":
                    await ApplyImportedSaleAsync(context, payloadJson, actorUserId, cancellationToken);
                    break;
                case "Import.Receivable":
                    await ApplyImportedReceivableAsync(context, payloadJson, cancellationToken);
                    break;
                case "Import.Movement":
                    await ApplyImportedMovementAsync(context, payloadJson, actorUserId, cancellationToken);
                    break;
                case "Import.Payment":
                    await ApplyImportedPaymentAsync(context, payloadJson, actorUserId, cancellationToken);
                    break;
                case "Import.Discrepancy":
                    await ApplyImportedDiscrepancyAsync(context, payloadJson, actorUserId, cancellationToken);
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
        if (!ProductImageRules.IsSupported(payload.ImageData))
            throw new BusinessOperationConflictException("La imagen del producto no es JPG o PNG válida, o supera 512 KB.");
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
            product.ImageData = payload.ImageData;
            return;
        }
        if (operationType == "Product.Created")
        {
            if (!CatalogMatches(product, payload))
                throw new BusinessOperationConflictException("Ya existe un producto con ese identificador y datos diferentes.");
            return;
        }
        if (operationType != "Product.Resolved" && CatalogMatches(product, payload)) return;
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
        if (payload.ImageDataSpecified)
            product.ImageData = payload.ImageData;
    }

    private static bool CatalogMatches(ProductEntity product, ProductChangedPayload payload) =>
        product.Name == payload.Name && product.SalePriceMinorUnits == payload.SalePriceMinorUnits && product.Unit == payload.Unit &&
        product.CategoryId == payload.CategoryId && product.MinimumStockMilliUnits == payload.MinimumStockMilliUnits &&
        product.IsActive == payload.IsActive &&
        (!payload.ImageDataSpecified || ImagesEqual(product.ImageData, payload.ImageData));

    private static bool CatalogMatches(ProductEntity product, ProductCatalogSnapshot snapshot) =>
        product.Name == snapshot.Name && product.SalePriceMinorUnits == snapshot.SalePriceMinorUnits && product.Unit == snapshot.Unit &&
        product.CategoryId == snapshot.CategoryId && product.MinimumStockMilliUnits == snapshot.MinimumStockMilliUnits &&
        product.IsActive == snapshot.IsActive &&
        (!snapshot.ImageDataSpecified || ImagesEqual(product.ImageData, snapshot.ImageData));

    private static bool ImagesEqual(byte[]? left, byte[]? right) =>
        left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);

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
        var existing = await context.Purchases.AsNoTracking().SingleOrDefaultAsync(item => item.OperationId == operationId, ct);
        if (existing is not null)
        {
            var lines = await context.PurchaseLines.AsNoTracking().Where(item => item.OperationId == operationId).ToListAsync(ct);
            if (existing.OccurredAtUtc != payload.OccurredAtUtc || lines.Count != payload.Lines.Count ||
                payload.Lines.Any(incoming => !lines.Any(saved => saved.LineNumber == incoming.LineNumber &&
                    saved.ProductId == incoming.ProductId && saved.QuantityMilliUnits == incoming.QuantityMilliUnits &&
                    (string.IsNullOrWhiteSpace(incoming.ProductNameSnapshot) || saved.ProductNameSnapshot == incoming.ProductNameSnapshot) &&
                    (string.IsNullOrWhiteSpace(incoming.UnitSnapshot) || saved.UnitSnapshot == incoming.UnitSnapshot))))
                throw new BusinessOperationConflictException("El identificador de compra ya existe con contenido diferente o incompleto.");
            return;
        }
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
            if (line.Type == InventoryMovementType.MixedWaste)
            {
                if (line.ProductId != Guid.Empty || line.QuantityMilliUnits <= 0 ||
                    string.IsNullOrWhiteSpace(line.UnitSnapshot) ||
                    string.IsNullOrWhiteSpace(line.Reason) ||
                    !line.Reason.StartsWith("Merma mixta", StringComparison.Ordinal))
                    throw new BusinessOperationConflictException("La merma mixta contiene datos inválidos.");
                context.InventoryMovements.Add(new InventoryMovementEntity
                {
                    MovementId = Guid.NewGuid(), OperationId = operationId, LineNumber = line.LineNumber,
                    ProductId = null, ProductNameSnapshot = "Merma mixta (no atribuida)", UnitSnapshot = line.UnitSnapshot,
                    Type = (int)InventoryMovementType.MixedWaste, QuantityMilliUnits = line.QuantityMilliUnits,
                    Reason = line.Reason, OccurredAtUtc = payload.OccurredAtUtc, ActorUserId = actor
                });
                continue;
            }

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

    private static async Task ApplyMixedWasteAsync(LocalDbContext context, Guid operationId, string json, Guid actor, CancellationToken ct)
    {
        var payload = Read<InventoryOperationPayload>(json);
        if (payload.OperationId != operationId || payload.Lines.Count != 1 || payload.OccurredAtUtc.Kind != DateTimeKind.Utc)
            throw new BusinessOperationConflictException("La merma mixta está incompleta.");

        var line = payload.Lines[0];
        if (line.Type != InventoryMovementType.MixedWaste || line.ProductId != Guid.Empty || line.QuantityMilliUnits <= 0 ||
            string.IsNullOrWhiteSpace(line.UnitSnapshot) || string.IsNullOrWhiteSpace(line.Reason) ||
            !line.Reason.StartsWith("Merma mixta", StringComparison.Ordinal))
            throw new BusinessOperationConflictException("La merma mixta contiene datos inválidos.");

        context.InventoryMovements.Add(new InventoryMovementEntity
        {
            MovementId = Guid.NewGuid(), OperationId = operationId, LineNumber = line.LineNumber,
            ProductId = null, ProductNameSnapshot = "Merma mixta (no atribuida)", UnitSnapshot = line.UnitSnapshot,
            Type = (int)InventoryMovementType.MixedWaste, QuantityMilliUnits = line.QuantityMilliUnits,
            Reason = line.Reason, OccurredAtUtc = payload.OccurredAtUtc, ActorUserId = actor
        });
        await Task.CompletedTask;
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

    private static async Task ApplyImportedCategoryAsync(LocalDbContext context, string json, CancellationToken ct)
    {
        var payload = Read<InitialImportCategory>(json);
        var name = payload.Name.Trim();
        if (payload.CategoryId == Guid.Empty || name.Length == 0)
            throw new BusinessOperationConflictException("La categoría importada contiene datos inválidos.");
        var existing = await context.Categories.SingleOrDefaultAsync(item => item.CategoryId == payload.CategoryId, ct);
        if (existing is not null)
        {
            if (existing.Name != name)
                throw new BusinessOperationConflictException("La categoría importada ya existe con el mismo identificador y otro nombre.");
            return;
        }
        var normalized = name.ToUpperInvariant();
        if (await context.Categories.AnyAsync(item => item.NormalizedName == normalized, ct))
            throw new BusinessOperationConflictException("Ya existe una categoría con ese nombre y otro identificador.");
        context.Categories.Add(new CategoryEntity { CategoryId = payload.CategoryId, Name = name, NormalizedName = normalized });
    }

    private static async Task ApplyImportedProductAsync(LocalDbContext context, string json, CancellationToken ct)
    {
        var payload = Read<InitialImportProduct>(json);
        if (payload.ProductId == Guid.Empty || payload.CategoryId == Guid.Empty || string.IsNullOrWhiteSpace(payload.Name) ||
            string.IsNullOrWhiteSpace(payload.Unit) || payload.SalePriceMinorUnits < 0 || payload.StockMilliUnits < 0 ||
            payload.MinimumStockMilliUnits < 0 || !ProductImageRules.IsSupported(payload.ImageData))
            throw new BusinessOperationConflictException("El producto importado contiene datos inválidos.");
        if (!await context.Categories.AnyAsync(item => item.CategoryId == payload.CategoryId, ct))
            throw new BusinessOperationConflictException("La categoría del producto importado no existe.");
        var product = await context.Products.SingleOrDefaultAsync(item => item.ProductId == payload.ProductId, ct);
        if (product is null)
        {
            context.Products.Add(new ProductEntity { ProductId = payload.ProductId, Name = payload.Name.Trim(),
                SalePriceMinorUnits = payload.SalePriceMinorUnits, StockMilliUnits = payload.StockMilliUnits,
                Unit = payload.Unit.Trim(), CategoryId = payload.CategoryId,
                MinimumStockMilliUnits = payload.MinimumStockMilliUnits, IsActive = payload.IsActive,
                ImageData = payload.ImageData });
            return;
        }
        if (product.Name != payload.Name.Trim() || product.SalePriceMinorUnits != payload.SalePriceMinorUnits ||
            product.Unit != payload.Unit.Trim() || product.CategoryId != payload.CategoryId ||
            product.MinimumStockMilliUnits != payload.MinimumStockMilliUnits || product.IsActive != payload.IsActive ||
            !ImagesEqual(product.ImageData, payload.ImageData))
            throw new BusinessOperationConflictException("El producto importado ya existe con el mismo identificador y otros datos de catálogo.");

        // This event carries the administrator-approved stock snapshot, so clients converge
        // on the same balance while imported movement rows below remain historical only.
        product.StockMilliUnits = payload.StockMilliUnits;
        product.ImageData = payload.ImageData;
    }

    private static async Task ApplyImportedCustomerAsync(LocalDbContext context, string json, CancellationToken ct)
    {
        var payload = Read<InitialImportCustomer>(json);
        var name = payload.Name.Trim();
        if (payload.CustomerId == Guid.Empty || name.Length == 0 || payload.CreatedAtUtc.Kind != DateTimeKind.Utc)
            throw new BusinessOperationConflictException("El cliente importado contiene datos inválidos.");
        var existing = await context.Customers.SingleOrDefaultAsync(item => item.CustomerId == payload.CustomerId, ct);
        if (existing is not null)
        {
            if (existing.Name != name || existing.Phone != payload.Phone || existing.CreatedAtUtc != payload.CreatedAtUtc)
                throw new BusinessOperationConflictException("El cliente importado ya existe con el mismo identificador y otros datos.");
            return;
        }
        var normalized = name.ToUpperInvariant();
        if (await context.Customers.AnyAsync(item => item.NormalizedName == normalized, ct))
            throw new BusinessOperationConflictException("Ya existe un cliente con ese nombre y otro identificador.");
        context.Customers.Add(new CustomerEntity { CustomerId = payload.CustomerId, Name = name,
            NormalizedName = normalized, Phone = payload.Phone, CreatedAtUtc = payload.CreatedAtUtc });
    }

    private static async Task ApplyImportedPurchaseAsync(LocalDbContext context, string json, Guid actor, CancellationToken ct)
    {
        var payload = Read<InitialImportPurchasePayload>(json);
        if (payload.OperationId == Guid.Empty || payload.OccurredAtUtc.Kind != DateTimeKind.Utc ||
            payload.Lines.Count == 0 || payload.Lines.Select(item => item.LineNumber).Distinct().Count() != payload.Lines.Count ||
            payload.Lines.Any(item => item.LineNumber < 0 || item.ProductId == Guid.Empty || item.QuantityMilliUnits <= 0))
            throw new BusinessOperationConflictException("La compra importada está incompleta o contiene cantidades inválidas.");
        if (!await context.UserAccounts.AnyAsync(item => item.UserId == actor, ct))
            throw new BusinessOperationConflictException("No existe la cuenta central elegida como responsable de esta compra.");
        var existing = await context.Purchases.SingleOrDefaultAsync(item => item.OperationId == payload.OperationId, ct);
        if (existing is not null)
        {
            var lines = await context.PurchaseLines.AsNoTracking().Where(item => item.OperationId == payload.OperationId).ToListAsync(ct);
            if (existing.OccurredAtUtc != payload.OccurredAtUtc || existing.ActorUserId != actor ||
                lines.Count != payload.Lines.Count || payload.Lines.Any(incoming => !lines.Any(saved =>
                    saved.LineNumber == incoming.LineNumber && saved.ProductId == incoming.ProductId &&
                    saved.ProductNameSnapshot == incoming.ProductNameSnapshot && saved.UnitSnapshot == incoming.UnitSnapshot &&
                    saved.QuantityMilliUnits == incoming.QuantityMilliUnits)))
                throw new BusinessOperationConflictException("La compra ya existe con el mismo identificador y otros datos.");
            return;
        }
        foreach (var line in payload.Lines)
            if (!await context.Products.AnyAsync(item => item.ProductId == line.ProductId, ct))
                throw new BusinessOperationConflictException("Un producto de la compra importada no existe.");
        context.Purchases.Add(new PurchaseEntity { OperationId = payload.OperationId,
            OccurredAtUtc = payload.OccurredAtUtc, ActorUserId = actor });
        context.PurchaseLines.AddRange(payload.Lines.Select(line => new PurchaseLineEntity
        {
            OperationId = payload.OperationId, LineNumber = line.LineNumber, ProductId = line.ProductId,
            ProductNameSnapshot = line.ProductNameSnapshot, UnitSnapshot = line.UnitSnapshot,
            QuantityMilliUnits = line.QuantityMilliUnits
        }));
    }

    private static async Task ApplyImportedSaleAsync(LocalDbContext context, string json, Guid actor, CancellationToken ct)
    {
        var payload = Read<InitialImportSalePayload>(json);
        if (payload.OperationId == Guid.Empty || payload.OccurredAtUtc.Kind != DateTimeKind.Utc ||
            payload.TotalMinorUnits < 0 || payload.Lines.Count == 0 ||
            payload.Lines.Select(item => item.LineNumber).Distinct().Count() != payload.Lines.Count ||
            payload.Lines.Any(item => item.LineNumber < 0 || item.ProductId == Guid.Empty || item.QuantityMilliUnits <= 0 ||
                item.SalePriceMinorUnits < 0 || item.LineTotalMinorUnits < 0) ||
            payload.Lines.Sum(item => item.LineTotalMinorUnits) != payload.TotalMinorUnits)
            throw new BusinessOperationConflictException("La venta importada está incompleta o contiene importes inválidos.");
        if (!await context.UserAccounts.AnyAsync(item => item.UserId == actor, ct))
            throw new BusinessOperationConflictException("No existe la cuenta central elegida como responsable de esta venta.");
        if (payload.CustomerId is Guid customerId && !await context.Customers.AnyAsync(item => item.CustomerId == customerId, ct))
            throw new BusinessOperationConflictException("El cliente de la venta importada no existe.");
        var existing = await context.Sales.SingleOrDefaultAsync(item => item.OperationId == payload.OperationId, ct);
        if (existing is not null)
        {
            var lines = await context.SaleLines.AsNoTracking().Where(item => item.OperationId == payload.OperationId).ToListAsync(ct);
            if (existing.OccurredAtUtc != payload.OccurredAtUtc || existing.ActorUserId != actor ||
                existing.TotalMinorUnits != payload.TotalMinorUnits || existing.IsCredit != payload.IsCredit ||
                existing.CustomerId != payload.CustomerId || lines.Count != payload.Lines.Count ||
                payload.Lines.Any(incoming => !lines.Any(saved => saved.LineNumber == incoming.LineNumber &&
                    saved.ProductId == incoming.ProductId && saved.ProductNameSnapshot == incoming.ProductNameSnapshot &&
                    saved.UnitSnapshot == incoming.UnitSnapshot && saved.QuantityMilliUnits == incoming.QuantityMilliUnits &&
                    saved.SalePriceMinorUnits == incoming.SalePriceMinorUnits && saved.LineTotalMinorUnits == incoming.LineTotalMinorUnits)))
                throw new BusinessOperationConflictException("La venta ya existe con el mismo identificador y otros datos.");
            return;
        }
        foreach (var line in payload.Lines)
            if (!await context.Products.AnyAsync(item => item.ProductId == line.ProductId, ct))
                throw new BusinessOperationConflictException("Un producto de la venta importada no existe.");
        context.Sales.Add(new SaleEntity { OperationId = payload.OperationId, OccurredAtUtc = payload.OccurredAtUtc,
            ActorUserId = actor, TotalMinorUnits = payload.TotalMinorUnits, IsCredit = payload.IsCredit,
            CustomerId = payload.CustomerId });
        context.SaleLines.AddRange(payload.Lines.Select(line => new SaleLineEntity
        {
            OperationId = payload.OperationId, LineNumber = line.LineNumber, ProductId = line.ProductId,
            ProductNameSnapshot = line.ProductNameSnapshot, UnitSnapshot = line.UnitSnapshot,
            QuantityMilliUnits = line.QuantityMilliUnits, SalePriceMinorUnits = line.SalePriceMinorUnits,
            LineTotalMinorUnits = line.LineTotalMinorUnits
        }));
    }

    private static async Task ApplyImportedReceivableAsync(LocalDbContext context, string json, CancellationToken ct)
    {
        var payload = Read<InitialImportReceivable>(json);
        if (payload.OperationId == Guid.Empty || payload.CustomerId == Guid.Empty || payload.OriginalAmountMinorUnits < 0 ||
            payload.OutstandingMinorUnits < 0 || payload.OutstandingMinorUnits > payload.OriginalAmountMinorUnits ||
            payload.DueAtUtc.Kind != DateTimeKind.Utc)
            throw new BusinessOperationConflictException("La cuenta por cobrar importada contiene datos inválidos.");
        var existing = await context.Receivables.SingleOrDefaultAsync(item => item.OperationId == payload.OperationId, ct);
        if (existing is not null)
        {
            if (existing.CustomerId != payload.CustomerId || existing.OriginalAmountMinorUnits != payload.OriginalAmountMinorUnits ||
                existing.OutstandingMinorUnits != payload.OutstandingMinorUnits || existing.DueAtUtc != payload.DueAtUtc)
                throw new BusinessOperationConflictException("La cuenta por cobrar ya existe con datos diferentes.");
            return;
        }
        if (!await context.Sales.AnyAsync(item => item.OperationId == payload.OperationId, ct) ||
            !await context.Customers.AnyAsync(item => item.CustomerId == payload.CustomerId, ct))
            throw new BusinessOperationConflictException("Falta la venta o el cliente relacionado con la cuenta por cobrar.");
        context.Receivables.Add(new ReceivableEntity { OperationId = payload.OperationId, CustomerId = payload.CustomerId,
            OriginalAmountMinorUnits = payload.OriginalAmountMinorUnits, OutstandingMinorUnits = payload.OutstandingMinorUnits,
            DueAtUtc = payload.DueAtUtc });
    }

    private static async Task ApplyImportedMovementAsync(LocalDbContext context, string json, Guid actor, CancellationToken ct)
    {
        var payload = Read<InitialImportMovementPayload>(json);
        if (payload.MovementId == Guid.Empty || payload.OperationId == Guid.Empty || payload.LineNumber < 0 ||
            payload.QuantityMilliUnits <= 0 || payload.OccurredAtUtc.Kind != DateTimeKind.Utc ||
            !Enum.IsDefined(typeof(InventoryMovementType), payload.Type))
            throw new BusinessOperationConflictException("El movimiento importado contiene datos inválidos.");
        if (!await context.UserAccounts.AnyAsync(item => item.UserId == actor, ct))
            throw new BusinessOperationConflictException("No existe la cuenta central elegida como responsable del movimiento.");
        if (payload.ProductId is Guid productId && !await context.Products.AnyAsync(item => item.ProductId == productId, ct))
            throw new BusinessOperationConflictException("El producto del movimiento importado no existe.");
        var sameLine = await context.InventoryMovements.SingleOrDefaultAsync(item =>
            item.OperationId == payload.OperationId && item.LineNumber == payload.LineNumber, ct);
        if (sameLine is not null)
        {
            if (sameLine.ProductId != payload.ProductId || sameLine.ProductNameSnapshot != payload.ProductNameSnapshot ||
                sameLine.UnitSnapshot != payload.UnitSnapshot || sameLine.Type != payload.Type ||
                sameLine.QuantityMilliUnits != payload.QuantityMilliUnits || sameLine.Reason != payload.Reason ||
                sameLine.OccurredAtUtc != payload.OccurredAtUtc || sameLine.ActorUserId != actor)
                throw new BusinessOperationConflictException("Ya existe un movimiento para la misma operación y renglón con datos diferentes.");
            if (sameLine.MovementId != payload.MovementId)
            {
                if (await context.InventoryMovements.AnyAsync(item => item.MovementId == payload.MovementId, ct))
                    throw new BusinessOperationConflictException("El identificador original del movimiento ya pertenece a otro registro.");
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE \"InventoryMovements\" SET \"MovementId\" = {payload.MovementId} WHERE \"MovementId\" = {sameLine.MovementId}", ct);
            }
            return;
        }
        if (await context.InventoryMovements.AnyAsync(item => item.MovementId == payload.MovementId, ct))
            throw new BusinessOperationConflictException("El identificador del movimiento ya pertenece a otro registro.");
        context.InventoryMovements.Add(new InventoryMovementEntity { MovementId = payload.MovementId,
            OperationId = payload.OperationId, LineNumber = payload.LineNumber, ProductId = payload.ProductId,
            ProductNameSnapshot = payload.ProductNameSnapshot, UnitSnapshot = payload.UnitSnapshot,
            Type = payload.Type, QuantityMilliUnits = payload.QuantityMilliUnits, Reason = payload.Reason,
            OccurredAtUtc = payload.OccurredAtUtc, ActorUserId = actor });
    }

    private static async Task ApplyImportedPaymentAsync(LocalDbContext context, string json, Guid actor, CancellationToken ct)
    {
        var payload = Read<InitialImportPaymentPayload>(json);
        if (payload.PaymentId == Guid.Empty || payload.OperationId == Guid.Empty || payload.AmountMinorUnits <= 0 ||
            payload.OccurredAtUtc.Kind != DateTimeKind.Utc ||
            !await context.UserAccounts.AnyAsync(item => item.UserId == actor, ct))
            throw new BusinessOperationConflictException("El pago importado contiene datos inválidos o un responsable inexistente.");
        var existing = await context.Payments.SingleOrDefaultAsync(item => item.PaymentId == payload.PaymentId, ct);
        if (existing is not null)
        {
            if (existing.OperationId != payload.OperationId || existing.ActorUserId != actor ||
                existing.AmountMinorUnits != payload.AmountMinorUnits || existing.OccurredAtUtc != payload.OccurredAtUtc)
                throw new BusinessOperationConflictException("El pago ya existe con el mismo identificador y otros datos.");
            return;
        }
        if (!await context.Receivables.AnyAsync(item => item.OperationId == payload.OperationId, ct))
            throw new BusinessOperationConflictException("La cuenta por cobrar del pago importado no existe.");
        context.Payments.Add(new PaymentEntity { PaymentId = payload.PaymentId, OperationId = payload.OperationId,
            ActorUserId = actor, AmountMinorUnits = payload.AmountMinorUnits, OccurredAtUtc = payload.OccurredAtUtc });
    }

    private static async Task ApplyImportedDiscrepancyAsync(LocalDbContext context, string json, Guid actor, CancellationToken ct)
    {
        var payload = Read<InitialImportDiscrepancyPayload>(json);
        if (payload.DiscrepancyId == Guid.Empty || payload.OperationId == Guid.Empty || payload.ProductId == Guid.Empty ||
            payload.ShortageMilliUnits <= 0 || payload.CreatedAtUtc.Kind != DateTimeKind.Utc ||
            !await context.UserAccounts.AnyAsync(item => item.UserId == actor, ct) ||
            !await context.Products.AnyAsync(item => item.ProductId == payload.ProductId, ct))
            throw new BusinessOperationConflictException("La diferencia de inventario importada contiene datos inválidos o referencias faltantes.");
        var existing = await context.InventoryDiscrepancies.SingleOrDefaultAsync(item => item.DiscrepancyId == payload.DiscrepancyId, ct);
        if (existing is not null)
        {
            if (existing.OperationId != payload.OperationId || existing.ProductId != payload.ProductId ||
                existing.ActorUserId != actor || existing.ShortageMilliUnits != payload.ShortageMilliUnits ||
                existing.CreatedAtUtc != payload.CreatedAtUtc || existing.IsResolved != payload.IsResolved)
                throw new BusinessOperationConflictException("La diferencia de inventario ya existe con datos diferentes.");
            return;
        }
        context.InventoryDiscrepancies.Add(new InventoryDiscrepancyEntity { DiscrepancyId = payload.DiscrepancyId,
            OperationId = payload.OperationId, ProductId = payload.ProductId, ActorUserId = actor,
            ShortageMilliUnits = payload.ShortageMilliUnits, CreatedAtUtc = payload.CreatedAtUtc, IsResolved = payload.IsResolved });
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
