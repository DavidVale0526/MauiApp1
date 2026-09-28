using System.Text.Json;
using MauiApp1.Application.Authentication;
using MauiApp1.Application.Inventory;
using MauiApp1.Application.Storage;
using MauiApp1.Domain.Inventory;
using MauiApp1.Infrastructure.Persistence;
using MauiApp1.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MauiApp1.Infrastructure.Products;

internal sealed class EfInventoryMovementService(
    IDbContextFactory<LocalDbContext> contextFactory,
    ILocalDeviceIdentity deviceIdentity,
    IAuthenticationService authenticationService) : IInventoryMovementService
{
    private const string OutboxOperationType = "Inventory.Recorded";

    public async Task<InventoryOperationResult> ApplyAsync(
        InventoryOperationDraft operation,
        CancellationToken cancellationToken = default)
    {
        var actor = authenticationService.CurrentUser;
        if (actor is null)
        {
            return InventoryOperationResult.Failure("Inicia sesión para registrar movimientos de inventario.");
        }

        if (!TryValidate(operation, out var normalizedLines, out var validationError))
        {
            return InventoryOperationResult.Failure(validationError);
        }

        var payload = new InventoryOperationPayload(
            operation.OperationId,
            operation.OccurredAtUtc,
            normalizedLines.Select(line => new InventoryMovementPayloadLine(
                line.LineNumber,
                line.ProductId.Value,
                line.Type,
                line.Quantity.MilliUnits,
                line.Reason)).ToArray());
        var payloadJson = JsonSerializer.Serialize(payload);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var existingOperation = await context.OutboxMessages
            .AsNoTracking()
            .SingleOrDefaultAsync(message => message.OperationId == operation.OperationId, cancellationToken);
        if (existingOperation is not null)
        {
            return existingOperation.ActorUserId == actor.Id.Value &&
                   existingOperation.OperationType == OutboxOperationType &&
                   existingOperation.PayloadJson == payloadJson
                ? InventoryOperationResult.Success(alreadyApplied: true)
                : InventoryOperationResult.Failure("El identificador de operación ya se usó con otro contenido.");
        }

        if (await context.InventoryMovements.AnyAsync(
                movement => movement.OperationId == operation.OperationId,
                cancellationToken))
        {
            return InventoryOperationResult.Failure("El historial contiene una operación incompleta; no se modificó el inventario.");
        }

        var productIds = normalizedLines.Select(line => line.ProductId.Value).Distinct().ToArray();
        var products = await context.Products
            .Where(product => productIds.Contains(product.ProductId))
            .ToDictionaryAsync(product => product.ProductId, cancellationToken);
        if (products.Count != productIds.Length)
        {
            return InventoryOperationResult.Failure("Uno de los productos ya no existe. Actualiza el catálogo e inténtalo de nuevo.");
        }

        var deltas = new Dictionary<Guid, long>();
        try
        {
            foreach (var line in normalizedLines)
            {
                var signedQuantity = line.Type == InventoryMovementType.PurchaseEntry
                    ? line.Quantity.MilliUnits
                    : checked(-line.Quantity.MilliUnits);
                deltas[line.ProductId.Value] = checked(deltas.GetValueOrDefault(line.ProductId.Value) + signedQuantity);
            }

            foreach (var (productId, delta) in deltas)
            {
                var resultingStock = checked(products[productId].StockMilliUnits + delta);
                if (delta < 0 && resultingStock < 0 && operation.NegativeStockPolicy == NegativeStockPolicy.Reject)
                {
                    return InventoryOperationResult.Failure("La salida supera la existencia disponible según la política de inventario.");
                }

                products[productId].StockMilliUnits = resultingStock;
            }
        }
        catch (OverflowException)
        {
            return InventoryOperationResult.Failure("La cantidad resultante supera el valor permitido.");
        }

        foreach (var line in normalizedLines)
        {
            context.InventoryMovements.Add(new InventoryMovementEntity
            {
                MovementId = Guid.NewGuid(),
                OperationId = operation.OperationId,
                LineNumber = line.LineNumber,
                ProductId = line.ProductId.Value,
                Type = (int)line.Type,
                QuantityMilliUnits = line.Quantity.MilliUnits,
                Reason = line.Reason,
                OccurredAtUtc = operation.OccurredAtUtc,
                ActorUserId = actor.Id.Value
            });
        }

        var outbox = new EfOperationOutbox(context, deviceIdentity);
        await outbox.EnqueueAsync(
            new NewOutboxOperation(operation.OperationId, actor.Id.Value, OutboxOperationType, payloadJson),
            cancellationToken);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return InventoryOperationResult.Success();
        }
        catch (DbUpdateException)
        {
            return InventoryOperationResult.Failure("No se pudo guardar el movimiento completo. Inténtalo de nuevo con el mismo identificador de operación.");
        }
    }

    private static bool TryValidate(
        InventoryOperationDraft? operation,
        out InventoryMovementLineDraft[] lines,
        out string errorMessage)
    {
        lines = [];
        if (operation is null || operation.OperationId == Guid.Empty)
        {
            errorMessage = "La operación requiere un identificador estable.";
            return false;
        }

        if (operation.OccurredAtUtc.Kind != DateTimeKind.Utc)
        {
            errorMessage = "La fecha del movimiento debe estar expresada en UTC.";
            return false;
        }

        if (!Enum.IsDefined(operation.NegativeStockPolicy))
        {
            errorMessage = "La política de existencias negativas no es válida.";
            return false;
        }

        if (operation.Lines is null || operation.Lines.Count == 0)
        {
            errorMessage = "La operación debe contener al menos un movimiento.";
            return false;
        }

        lines = operation.Lines
            .OrderBy(line => line.LineNumber)
            .Select(line => line with { Reason = line.Reason?.Trim() })
            .ToArray();

        if (lines.Any(line => line.LineNumber < 0 || line.ProductId.IsEmpty ||
                              !Enum.IsDefined(line.Type) || line.Quantity.MilliUnits <= 0))
        {
            errorMessage = "Cada movimiento requiere producto, tipo, número de línea válido y cantidad mayor que cero.";
            return false;
        }

        if (lines.Select(line => line.LineNumber).Distinct().Count() != lines.Length)
        {
            errorMessage = "La operación contiene números de línea repetidos.";
            return false;
        }

        if (lines.Select(line => line.Type).Distinct().Count() != 1)
        {
            errorMessage = "Una operación debe contener movimientos del mismo tipo.";
            return false;
        }

        if (lines.Any(line => line.Type == InventoryMovementType.OtherExit && string.IsNullOrWhiteSpace(line.Reason)))
        {
            errorMessage = "Escribe el motivo de cada salida distinta de una venta.";
            return false;
        }

        if (lines.Any(line => line.Type != InventoryMovementType.OtherExit && line.Reason is not null))
        {
            errorMessage = "El motivo solo se registra para salidas distintas de una venta.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }
}
