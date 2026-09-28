namespace MauiApp1.Infrastructure.Persistence.Entities;

internal sealed class InventoryMovementEntity
{
    public Guid MovementId { get; set; }

    /// <summary>Identificador de operación estable compartido por todas sus líneas y su Outbox.</summary>
    public Guid OperationId { get; set; }

    public int LineNumber { get; set; }

    public Guid ProductId { get; set; }

    public string ProductNameSnapshot { get; set; } = string.Empty;

    public string UnitSnapshot { get; set; } = string.Empty;

    public int Type { get; set; }

    public long QuantityMilliUnits { get; set; }

    public string? Reason { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public Guid ActorUserId { get; set; }
}
