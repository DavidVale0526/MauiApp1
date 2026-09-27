namespace MauiApp1.Domain.Identifiers;

/// <summary>Identificador persistente de categoría, compartible entre instalaciones.</summary>
public readonly record struct CategoryId(Guid Value)
{
    public static CategoryId New() => new(Guid.NewGuid());

    public bool IsEmpty => Value == Guid.Empty;
}
