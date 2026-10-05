namespace MauiApp1.Domain.Inventory;

/// <summary>Tipo de cambio del saldo que representa cada línea del historial.</summary>
public enum InventoryMovementType
{
    PurchaseEntry = 1,
    SaleExit = 2,
    OtherExit = 3,
    /// <summary>Merma mezclada que no puede atribuirse a un producto concreto.</summary>
    MixedWaste = 4
}
