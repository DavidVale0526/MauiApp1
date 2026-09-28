using System.Text.Json;
using MauiApp1.Application.Authentication;
using MauiApp1.Application.Inventory;
using MauiApp1.Domain.Inventory;
using MauiApp1.Infrastructure.Inventory;
using MauiApp1.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MauiApp1.Infrastructure.Products;

internal sealed class EfInventoryMovementService(
    IDbContextFactory<LocalDbContext> contextFactory,
    InventoryMovementWriter movementWriter,
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

        if (!InventoryMovementWriter.TryValidate(operation, out var lines, out var validationError))
        {
            return InventoryOperationResult.Failure(validationError);
        }

        var payloadJson = JsonSerializer.Serialize(new InventoryOperationPayload(
            operation.OperationId,
            operation.OccurredAtUtc,
            lines.Select(line => new InventoryMovementPayloadLine(
                line.LineNumber, line.ProductId.Value, line.Type, line.Quantity.MilliUnits, line.Reason)).ToArray()));

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var result = await movementWriter.StageAsync(
            context, operation, actor.Id.Value, OutboxOperationType, payloadJson,
            requireActiveProducts: true, cancellationToken);
        if (!result.Succeeded || result.AlreadyApplied)
        {
            return result;
        }

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
}
