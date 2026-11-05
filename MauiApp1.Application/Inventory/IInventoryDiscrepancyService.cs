namespace MauiApp1.Application.Inventory;

/// <summary>
/// Ventas que se sincronizaron cuando ya no había existencia suficiente.
/// La venta se conserva (el dinero y el producto ya salieron), pero el faltante
/// queda a la vista del Administrador para que concilie el inventario.
/// </summary>
public interface IInventoryDiscrepancyService
{
    Task<InventoryDiscrepancySummary> GetSummaryAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InventoryDiscrepancyEntry>> GetOpenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Marca el faltante como revisado. La decisión viaja al coordinador para que
    /// los demás dispositivos dejen de mostrarlo.
    /// </summary>
    Task<string?> ResolveAsync(Guid discrepancyId, CancellationToken cancellationToken = default);
}

public sealed record InventoryDiscrepancySummary(int OpenCount, long TotalShortageMilliUnits);

public sealed record InventoryDiscrepancyEntry(
    Guid DiscrepancyId,
    Guid SaleOperationId,
    string ProductName,
    string Unit,
    long ShortageMilliUnits,
    DateTime OccurredAtUtc,
    long SaleTotalMinorUnits,
    string ActorUserName);
