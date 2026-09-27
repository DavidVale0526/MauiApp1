namespace MauiApp1.Domain.Values;

/// <summary>Importe exacto guardado en unidades menores (dos posiciones decimales); no fija una moneda.</summary>
public readonly record struct MoneyAmount(long MinorUnits)
{
    public const int DecimalPlaces = 2;

    public static MoneyAmount FromDecimal(decimal value)
    {
        var scaled = value * 100m;
        if (scaled != decimal.Truncate(scaled))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "El importe admite como máximo dos decimales.");
        }

        return new MoneyAmount(checked((long)scaled));
    }

    public decimal ToDecimal() => MinorUnits / 100m;
}
