namespace MauiApp1.Domain.Inventory;

/// <summary>Política explícita requerida por operaciones de salida; no se elige por omisión.</summary>
public enum NegativeStockPolicy
{
    Reject = 0,
    Allow = 1
}
