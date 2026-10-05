using MauiApp1.Application.Inventory;
using MauiApp1.Application.Storage;
using MauiApp1.Domain.Inventory;
using MauiApp1.Infrastructure.Persistence;
using MauiApp1.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MauiApp1.Infrastructure.Inventory;

internal sealed class InventoryMovementWriter(ILocalDeviceIdentity deviceIdentity)
{
    internal async Task<InventoryOperationResult> StageAsync(
        LocalDbContext context,
        InventoryOperationDraft operation,
        Guid actorUserId,
        string outboxOperationType,
        string payloadJson,
        bool requireActiveProducts,
        CancellationToken cancellationToken)
    {
        if (!TryValidate(operation, out var lines, out var errorMessage))
        {
            return InventoryOperationResult.Failure(errorMessage);
        }

        var existingOperation = await context.OutboxMessages.AsNoTracking()
            .SingleOrDefaultAsync(message => message.OperationId == operation.OperationId, cancellationToken);
        if (existingOperation is not null)
        {
            var same = existingOperation.ActorUserId == actorUserId &&
                       existingOperation.OperationType == outboxOperationType &&
                       existingOperation.PayloadJson == payloadJson;
            return same
                ? InventoryOperationResult.Success(alreadyApplied: true)
                : InventoryOperationResult.Failure("El identificador de operación ya se usó con otro contenido.");
        }

        if (await context.InventoryMovements.AnyAsync(
                movement => movement.OperationId == operation.OperationId, cancellationToken))
        {
            return InventoryOperationResult.Failure("El historial contiene una operación incompleta; no se modificó el inventario.");
        }

        var productIds = lines.Where(line => !line.ProductId.IsEmpty).Select(line => line.ProductId.Value).Distinct().ToArray();
        var products = await context.Products
            .Where(product => productIds.Contains(product.ProductId))
            .ToDictionaryAsync(product => product.ProductId, cancellationToken);
        if (products.Count != productIds.Length)
        {
            return InventoryOperationResult.Failure("Uno de los productos ya no existe. Actualiza el catálogo e inténtalo de nuevo.");
        }

        if (requireActiveProducts && products.Values.Any(product => !product.IsActive))
        {
            return InventoryOperationResult.Failure("Las compras solo pueden incluir productos activos.");
        }

        var deltas = new Dictionary<Guid, long>();
        try
        {
            foreach (var line in lines.Where(line => line.Type != InventoryMovementType.MixedWaste))
            {
                var delta = line.Type == InventoryMovementType.PurchaseEntry
                    ? line.Quantity.MilliUnits
                    : checked(-line.Quantity.MilliUnits);
                deltas[line.ProductId.Value] = checked(deltas.GetValueOrDefault(line.ProductId.Value) + delta);
            }

            foreach (var (productId, delta) in deltas)
            {
                var resultingStock = checked(products[productId].StockMilliUnits + delta);
                if (delta < 0 && resultingStock < 0 && operation.NegativeStockPolicy == NegativeStockPolicy.Reject)
                {
                    var product = products[productId];
                    var available = (product.StockMilliUnits / 1000m).ToString("0.000", System.Globalization.CultureInfo.CurrentCulture);
                    var requested = (-delta / 1000m).ToString("0.000", System.Globalization.CultureInfo.CurrentCulture);
                    return InventoryOperationResult.Failure(
                        $"Existencia insuficiente para {product.Name}. Disponible: {available} {product.Unit}; solicitada: {requested} {product.Unit}.");
                }

                products[productId].StockMilliUnits = resultingStock;
            }
        }
        catch (OverflowException)
        {
            return InventoryOperationResult.Failure("La cantidad resultante supera el valor permitido.");
        }

        foreach (var line in lines)
        {
            var product = line.ProductId.IsEmpty ? null : products[line.ProductId.Value];
            context.InventoryMovements.Add(new InventoryMovementEntity
            {
                MovementId = Guid.NewGuid(),
                OperationId = operation.OperationId,
                LineNumber = line.LineNumber,
                ProductId = line.ProductId.IsEmpty ? null : line.ProductId.Value,
                ProductNameSnapshot = line.ProductNameSnapshot ?? product?.Name ?? "Merma mixta (no atribuida)",
                UnitSnapshot = line.UnitSnapshot ?? product?.Unit ?? string.Empty,
                Type = (int)line.Type,
                QuantityMilliUnits = line.Quantity.MilliUnits,
                Reason = line.Reason,
                OccurredAtUtc = operation.OccurredAtUtc,
                ActorUserId = actorUserId
            });
        }

        var outbox = new EfOperationOutbox(context, deviceIdentity);
        await outbox.EnqueueAsync(
            new NewOutboxOperation(operation.OperationId, actorUserId, outboxOperationType, payloadJson),
            cancellationToken);
        return InventoryOperationResult.Success();
    }

    internal static bool TryValidate(
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
        if (lines.Any(line => line.LineNumber < 0 ||
                              (line.ProductId.IsEmpty && line.Type != InventoryMovementType.MixedWaste) ||
                              !Enum.IsDefined(line.Type) || line.Quantity.MilliUnits <= 0) ||
            lines.Select(line => line.LineNumber).Distinct().Count() != lines.Length ||
            lines.Select(line => line.Type).Distinct().Count() != 1)
        {
            errorMessage = "Cada movimiento requiere producto, tipo, línea única y cantidad mayor que cero; una operación debe usar un solo tipo.";
            return false;
        }

        if (lines.Any(line => (line.Type == InventoryMovementType.OtherExit || line.Type == InventoryMovementType.MixedWaste) && string.IsNullOrWhiteSpace(line.Reason)))
        {
            errorMessage = "Escribe el motivo de cada salida distinta de una venta.";
            return false;
        }

        if (lines.Any(line => line.Type == InventoryMovementType.OtherExit && !IsApprovedOtherExitReason(line.Reason)))
        {
            errorMessage = "Usa Vencimiento, Daño, Pérdida o una descripción que comience con «Otro:».";
            return false;
        }

        if (lines.Any(line => line.Type == InventoryMovementType.MixedWaste &&
                              (!line.ProductId.IsEmpty || string.IsNullOrWhiteSpace(line.UnitSnapshot) ||
                               line.Reason is null || !line.Reason.StartsWith("Merma mixta", StringComparison.Ordinal))) ||
            lines.Any(line => line.Type != InventoryMovementType.OtherExit && line.Type != InventoryMovementType.MixedWaste && line.Reason is not null))
        {
            errorMessage = "El motivo solo se registra para salidas distintas de una venta.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    private static bool IsApprovedOtherExitReason(string? reason) =>
        reason is "Vencimiento" or "Daño" or "Pérdida" ||
        (reason is not null && reason.StartsWith("Otro: ", StringComparison.Ordinal) &&
         !string.IsNullOrWhiteSpace(reason["Otro: ".Length..]));
}
