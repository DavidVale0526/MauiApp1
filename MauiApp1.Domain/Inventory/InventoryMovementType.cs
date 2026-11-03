namespace MauiApp1.Domain.Inventory;

/// <summary>Tipo de cambio del saldo que representa cada línea del historial.</summary>
public enum InventoryMovementType
{
    PurchaseEntry = 1,
    SaleExit = 2,
    OtherExit = 3,
    /// <summary>Merma mezclada que no puede atribuirse a un producto concreto.</summary>
    MixedWaste = 4,
    /// <summary>Producto devuelto que vuelve a estar disponible para venta.</summary>
    SaleReturnEntry = 5

    //merma de producto que lo hace el admin
}
