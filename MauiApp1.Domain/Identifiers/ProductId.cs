namespace MauiApp1.Domain.Identifiers;

/// <summary>Identificador persistente de producto, generado una sola vez en el dispositivo de origen.</summary>
public readonly record struct ProductId(Guid Value)
{
    public static ProductId New() => new(Guid.NewGuid());

    public bool IsEmpty => Value == Guid.Empty;
}
